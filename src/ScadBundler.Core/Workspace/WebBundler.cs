using System.Text;
using System.Text.RegularExpressions;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;

namespace ScadBundler.Core.Workspace;

/// <summary>
/// The browser-friendly bundle entry point: maps <see cref="WebBundleOptions"/> to
/// <see cref="BundleOptions"/> + <see cref="EmitOptions"/> exactly as the CLI's <c>BundleCommand</c> does,
/// runs <see cref="Bundler.Bundle(string, BundleOptions, IFileSystem)"/> (the <see cref="IFileSystem"/>
/// overload — no environment or disk) then <see cref="Emitter.Emit(Ast.ScadFile, EmitOptions?)"/>, and
/// projects diagnostics and stats. Output is <b>byte-identical</b> to the CLI for the same inputs. Never
/// throws.
/// </summary>
public static class WebBundler
{
    // Matches the "<n> definition[s] tree-shaken" fragment of the SB5009 hardening summary.
    private static readonly Regex TreeShakenPattern =
        new(@"(\d+) definitions? tree-shaken", RegexOptions.CultureInvariant);

    /// <summary>Bundles the project rooted at <paramref name="root"/> over <paramref name="fs"/>.</summary>
    /// <param name="fs">The in-memory file system (built by <see cref="ProjectAnalyzer"/>).</param>
    /// <param name="root">The root file's virtual path.</param>
    /// <param name="options">The browser-facing bundle options.</param>
    /// <param name="filesInlined">The distinct-non-root file count from a load the caller has already
    /// performed (<see cref="ProjectAnalysis.FilesInlined"/>); when supplied, the stats pass reuses it instead
    /// of re-loading the graph just to count (Slice W5 §C2). <c>null</c> ⇒ recompute it here.</param>
    /// <returns>The bundle text (or <c>""</c> when blocked by an Error diagnostic), diagnostics, and stats.</returns>
    public static WebBundleResult Bundle(
        InMemoryFileSystem fs, string root, WebBundleOptions options, int? filesInlined = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        HardeningProfile hardening = options.Hardening;

        // Option mapping — mirrors BundleCommand exactly. LibraryPaths is empty (browser sandbox; no
        // OPENSCADPATH). The IFileSystem overload of Bundler.Bundle never consults the environment.
        var bundleOptions = new BundleOptions(
            [],
            options.OnCollision,
            options.BundleLicenses,
            options.PreserveComments,
            hardening,
            options.StripLicense,
            ParametersFirst: options.ParametersFirst);

        // Emit: minify collapses whitespace + drops non-sticky comments; obfuscate keeps formatting but
        // drops ordinary comments (the aggregated license + Customizer fence are sticky and survive both).
        var emitOptions = new EmitOptions(
            Minify: hardening == HardeningProfile.Minify,
            PreserveComments: hardening == HardeningProfile.None && options.PreserveComments);

        BundleResult result = Bundler.Bundle(root, bundleOptions, fs);
        bool hasError = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        // An Error diagnostic produces no output (the CLI's exit-1 behavior).
        string text = hasError ? string.Empty : Emitter.Emit(result.Bundled, emitOptions);

        BundleStats stats = ComputeStats(fs, root, bundleOptions, result.Diagnostics, text, filesInlined);
        IReadOnlyList<DiagnosticDto> diagnostics = [.. result.Diagnostics.Select(ToDto)];

        return new WebBundleResult(text, !hasError, diagnostics, stats);
    }

    private static BundleStats ComputeStats(
        InMemoryFileSystem fs,
        string root,
        BundleOptions bundleOptions,
        IReadOnlyList<Diagnostic> diagnostics,
        string text,
        int? precomputedFilesInlined)
    {
        // FilesInlined: distinct non-root files in the load graph (exactly what --verbose reports). Reuse the
        // caller's count when it has one (ProjectAnalyzer already loaded the graph, Slice W5 §C2); otherwise
        // load once more to count — loading is independent of the collision/licence/hardening options.
        int filesInlined = precomputedFilesInlined ?? CountInlinedFiles(SourceLoader.Load(root, bundleOptions, fs));

        int renames = diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed);
        int normalizations = diagnostics.Count(d =>
            d.Code is DiagnosticCode.AssignNormalized or DiagnosticCode.ChildNormalized);
        int definitionsRemoved = TreeShakenCount(diagnostics);

        return new BundleStats(
            filesInlined,
            Encoding.UTF8.GetByteCount(text),
            renames,
            definitionsRemoved,
            normalizations);
    }

    // Distinct non-root files in the load graph — the same formula ProjectAnalyzer.FilesInlined uses, so a
    // recount here (when the caller supplies no precomputed value) matches the precomputed path exactly.
    private static int CountInlinedFiles(LoadGraph graph)
    {
        string rootPath = graph.Root.Source.Path;
        return graph.ByAbsolutePath.Values
            .Select(f => f.Source.Path)
            .Where(p => !string.Equals(p, rootPath, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    // The number of tree-shaken definitions, read from the SB5009 hardening summary message (the only
    // public surface that carries it). 0 when no hardening profile ran.
    private static int TreeShakenCount(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Code != DiagnosticCode.Hardened)
            {
                continue;
            }

            Match match = TreeShakenPattern.Match(diagnostic.Message);
            if (match.Success && int.TryParse(match.Groups[1].ValueSpan, out int removed))
            {
                return removed;
            }
        }

        return 0;
    }

    private static DiagnosticDto ToDto(Diagnostic d) => new(
        d.Code,
        d.Severity.ToString(),
        d.Message,
        d.Span.File.Path,
        d.Span.Start.Line,
        d.Span.Start.Column);
}
