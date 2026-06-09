using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// The output of <see cref="Bundler.Bundle(string, BundleOptions)"/>: the single flattened
/// <see cref="ScadFile"/> AST (the emitter renders it in Slice 6) plus every diagnostic from loading,
/// semantic analysis, and inlining, in source order. Bundling never throws — all problems are
/// diagnostics.
/// </summary>
/// <param name="Bundled">The flattened bundle AST.</param>
/// <param name="Diagnostics">Loader + semantic + inliner diagnostics, source-ordered.</param>
public sealed record BundleResult(ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics);
