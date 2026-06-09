using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Semantics;

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
            LibraryPaths = [.. options.LibraryPaths, .. OpenScadPathEntries()],
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

        IReadOnlyList<Diagnostic> all =
        [
            .. graph.Diagnostics
                .Concat(semantics.Diagnostics)
                .Concat(inlinerDiagnostics)
                .OrderBy(d => d.Span.File.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Span.Start.Offset)
                .ThenBy(d => d.Code, StringComparer.Ordinal),
        ];

        return new BundleResult(bundled, all);
    }

    private static string[] OpenScadPathEntries()
    {
        string? value = Environment.GetEnvironmentVariable("OPENSCADPATH");
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
