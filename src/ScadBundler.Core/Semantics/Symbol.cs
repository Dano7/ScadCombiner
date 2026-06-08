using ScadBundler.Core.Ast;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Semantics;

/// <summary>
/// Identifies a top-level (file-scope) declaration that the inliner may rename: a module, function,
/// or variable. Local bindings (parameters, <c>let</c>/<c>for</c>/comprehension bindings,
/// function-literal parameters, nested definitions) are never symbols — they resolve to <c>null</c>.
/// </summary>
/// <param name="Kind">Whether this is a module, function, or variable.</param>
/// <param name="Name">The declared name.</param>
/// <param name="File">The file the declaration lives in (provenance survives inlining).</param>
/// <param name="Declaration">
/// The declaring node: a <see cref="ModuleDefinition"/>, <see cref="FunctionDefinition"/>, or
/// <see cref="AssignmentStatement"/>. Reference identity of this node keys the references-to table.
/// </param>
public sealed record Symbol(SymbolKind Kind, string Name, SourceFile File, AstNode Declaration);
