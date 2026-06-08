using ScadBundler.Core.Diagnostics;

namespace ScadBundler.Core.Lexing;

/// <summary>
/// The result of lexing a source file: the token stream (always ending in a single
/// <see cref="TokenKind.Eof"/> token) and any diagnostics collected during scanning.
/// </summary>
/// <param name="Tokens">The token stream, terminated by exactly one EOF token.</param>
/// <param name="Diagnostics">Diagnostics collected while scanning (the lexer never throws).</param>
public sealed record LexResult(IReadOnlyList<Token> Tokens, IReadOnlyList<Diagnostic> Diagnostics);
