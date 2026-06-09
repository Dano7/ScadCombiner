using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Semantics;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// Drives the inliner over an in-memory <see cref="LoadGraph"/> (built by <see cref="SemanticHelper"/>,
/// edges resolved by raw path), so Slice-5 bundle assertions can be written without disk I/O. The
/// produced <see cref="ScadFile"/> is asserted on directly (AST presence/absence/rewrite); exact text
/// becomes a golden only once the Slice-6 emitter locks formatting.
/// </summary>
public static class BundleHelper
{
    /// <summary>Builds the graph from <paramref name="files"/> (first is root), analyzes, and inlines.</summary>
    /// <param name="options">Bundle options (defaults to <see cref="BundleOptions.Default"/>).</param>
    /// <param name="files">Named source files; the first is the root.</param>
    /// <returns>The bundled file and inliner diagnostics.</returns>
    public static (ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics) Bundle(
        BundleOptions? options, params (string Name, string Source)[] files)
    {
        LoadGraph graph = SemanticHelper.Graph(files);
        ISemanticModel model = SemanticAnalyzer.Analyze(graph).Model;
        return Inliner.Bundle(graph, model, options ?? BundleOptions.Default);
    }

    /// <summary>The top-level statements of the bundle.</summary>
    /// <param name="files">Named source files; the first is the root.</param>
    /// <returns>The bundled top-level statements.</returns>
    public static IReadOnlyList<Statement> Statements(params (string Name, string Source)[] files) =>
        Bundle(null, files).Bundled.Statements;

    /// <summary>Every diagnostic code produced by the inliner, in order.</summary>
    /// <param name="result">The bundle result.</param>
    /// <returns>The diagnostic codes.</returns>
    public static IReadOnlyList<string> Codes((ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics) result) =>
        [.. result.Diagnostics.Select(d => d.Code)];

    /// <summary>All module/function/variable names declared at the top level of the bundle.</summary>
    /// <param name="bundled">The bundled file.</param>
    /// <returns>Top-level declared names.</returns>
    public static IReadOnlyList<string> TopLevelDeclarationNames(ScadFile bundled) =>
    [
        .. bundled.Statements.Select(s => s switch
        {
            ModuleDefinition m => m.Name,
            FunctionDefinition f => f.Name,
            AssignmentStatement a => a.Name,
            _ => null,
        }).OfType<string>(),
    ];
}
