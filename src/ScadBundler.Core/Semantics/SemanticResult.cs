using ScadBundler.Core.Diagnostics;

namespace ScadBundler.Core.Semantics;

/// <summary>
/// The output of <see cref="SemanticAnalyzer"/>: the queryable <see cref="ISemanticModel"/> plus every
/// diagnostic produced during analysis. The analyzer never throws — all problems are diagnostics.
/// </summary>
/// <param name="Model">The semantic model (declarations, bindings, reachability).</param>
/// <param name="Diagnostics">Semantic diagnostics, in source order.</param>
public sealed record SemanticResult(ISemanticModel Model, IReadOnlyList<Diagnostic> Diagnostics);
