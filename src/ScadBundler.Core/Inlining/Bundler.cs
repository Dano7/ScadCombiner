using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Transforming;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// The bundler's public entry point: the full pipeline
/// <c>SourceLoader → Lexer/Parser → SemanticAnalyzer → Inliner</c>, producing one flattened
/// <see cref="BundleResult"/>. Never throws — missing files, cycles, and parse errors in dependencies
/// all surface as diagnostics alongside a best-effort bundle.
/// </summary>
public static class Bundler
{
    /// <summary>Bundles the project rooted at <paramref name="rootPath"/> from disk, appending
    /// <c>OPENSCADPATH</c> entries to the configured library paths. Never throws.</summary>
    /// <param name="rootPath">The root <c>.scad</c> file.</param>
    /// <param name="options">Bundle options.</param>
    /// <returns>The bundle result (flattened AST + diagnostics).</returns>
    public static BundleResult Bundle(string rootPath, BundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BundleOptions effective = options with
        {
            LibraryPaths = [.. options.LibraryPaths, .. OpenScadEnvironment.LibraryPaths()],
        };
        return Bundle(rootPath, effective, DiskFileSystem.Instance);
    }

    /// <summary>Bundles through an explicit <see cref="IFileSystem"/> (the test seam; no environment
    /// inspection — the caller supplies every search path in <paramref name="options"/>). Never throws.</summary>
    /// <param name="rootPath">The root <c>.scad</c> file.</param>
    /// <param name="options">Bundle options.</param>
    /// <param name="fileSystem">The file-access seam.</param>
    /// <returns>The bundle result (flattened AST + diagnostics).</returns>
    public static BundleResult Bundle(string rootPath, BundleOptions options, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);

        LoadGraph graph = SourceLoader.Load(rootPath, options, fileSystem);
        SemanticResult semantics = SemanticAnalyzer.Analyze(graph);
        (Ast.ScadFile bundled, IReadOnlyList<Diagnostic> inlinerDiagnostics) =
            Inliner.Bundle(graph, semantics.Model, options);

        // Post-inline hardening (Slice 7): a no-op unless options.Hardening selects minify/obfuscate.
        var transformDiagnostics = new DiagnosticBag();
        Ast.ScadFile output = Transformer.Run(bundled, options.Hardening, transformDiagnostics);

        // The semantic pass (within-file scope) and the inliner (merged-set collisions) independently
        // detect the same within-file redefinition (SB3004) and report it with an identical code, span,
        // and message; collapse such exact duplicates so a finding surfaces once, not once per stage.
        IReadOnlyList<Diagnostic> all =
        [
            .. graph.Diagnostics
                .Concat(semantics.Diagnostics)
                .Concat(inlinerDiagnostics)
                .Concat(transformDiagnostics.ToList())
                .Where(d => options.Lint || !IsStaticLint(d))
                .OrderBy(d => d.Span.File.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Span.Start.Offset)
                .ThenBy(d => d.Code, StringComparer.Ordinal)
                .DistinctBy(d => (d.Code, d.Severity, d.Message, d.Span.File.Path, d.Span.Start.Offset, d.Span.End.Offset)),
        ];

        return new BundleResult(output, all);
    }

    // The static source-lint findings the bundler can derive but OpenSCAD does NOT report at parse time:
    // an unknown reference (SB3005) — OpenSCAD reads it as `undef`, warning only at evaluation time if the
    // expression is ever reached — and a module/function redefinition (SB3004) — OpenSCAD's flat scope
    // silently last-wins (parser.y warns only for VARIABLE reassignment, SB3003, which is kept). Both are
    // static approximations of a dynamic property, so they false-positive on dead code, short-circuited
    // reads, optional config variables, and intra-library duplicates in real libraries. They are
    // suppressed unless `--lint` (BundleOptions.Lint) is set; the underlying collision is still resolved.
    private static bool IsStaticLint(Diagnostic diagnostic) =>
        diagnostic.Code is DiagnosticCode.UnknownReference or DiagnosticCode.DefinitionRedefined;
}
