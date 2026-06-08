using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Loading;

/// <summary>
/// A resolved <c>include &lt;path&gt;</c> edge: the originating statement and the file it resolved to
/// (<c>null</c> when the path could not be found, in which case the statement is dropped at assembly).
/// </summary>
/// <param name="Statement">The <see cref="IncludeStatement"/> that produced this edge.</param>
/// <param name="Target">The included file, or <c>null</c> when unresolved.</param>
public sealed record IncludeEdge(IncludeStatement Statement, LoadedFile? Target);

/// <summary>
/// A resolved <c>use &lt;path&gt;</c> edge: the originating statement and the file it resolved to.
/// A font <c>use</c> (<c>.ttf</c>/<c>.otf</c>) is marked <see cref="FontPassthrough"/> and carries no
/// loaded target (it is emitted verbatim, never inlined).
/// </summary>
/// <param name="Statement">The <see cref="UseStatement"/> that produced this edge.</param>
/// <param name="Target">The used file, or <c>null</c> when unresolved or a font pass-through.</param>
/// <param name="FontPassthrough"><c>true</c> when the path is a binary font registered, not loaded.</param>
public sealed record UseEdge(UseStatement Statement, LoadedFile? Target, bool FontPassthrough = false);

/// <summary>
/// One loaded source file in a <see cref="LoadGraph"/>: its <see cref="SourceFile"/>, parsed
/// <see cref="ScadFile"/> AST, and the resolved <c>include</c>/<c>use</c> edges leaving it (in source
/// order). The semantic analyzer (Slice 4) consumes this; the <c>SourceLoader</c> (Slice 5) produces it.
/// </summary>
/// <param name="Source">The physical source file.</param>
/// <param name="Ast">The parsed AST root.</param>
/// <param name="Includes"><c>include</c> edges leaving this file, in source order.</param>
/// <param name="Uses"><c>use</c> edges leaving this file, in source order (resolution consults them last-first).</param>
public sealed record LoadedFile(
    SourceFile Source,
    ScadFile Ast,
    IReadOnlyList<IncludeEdge> Includes,
    IReadOnlyList<UseEdge> Uses);

/// <summary>
/// The load graph: a root file plus every file reachable from it via <c>include</c>/<c>use</c>, keyed
/// by absolute path (a file shared by many paths is loaded once). This is the cross-file input to the
/// <c>SemanticAnalyzer</c>; the full recursive loader that builds it lands in Slice 5.
/// </summary>
/// <param name="Root">The graph's root (the file bundling started from).</param>
/// <param name="ByAbsolutePath">Every loaded file, keyed by its absolute path.</param>
/// <param name="Diagnostics">Diagnostics produced while loading (resolution failures, cycles).</param>
public sealed record LoadGraph(
    LoadedFile Root,
    IReadOnlyDictionary<string, LoadedFile> ByAbsolutePath,
    IReadOnlyList<Diagnostic> Diagnostics);
