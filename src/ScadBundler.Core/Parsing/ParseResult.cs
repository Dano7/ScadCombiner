using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;

namespace ScadBundler.Core.Parsing;

/// <summary>
/// The result of parsing: the root <see cref="ScadFile"/> (always produced, possibly partial on
/// malformed input) and any diagnostics collected during parsing.
/// </summary>
/// <param name="Root">The parsed file root.</param>
/// <param name="Diagnostics">Diagnostics collected while parsing (and, for the convenience
/// overload, the lexer's diagnostics merged in first).</param>
public sealed record ParseResult(ScadFile Root, IReadOnlyList<Diagnostic> Diagnostics);
