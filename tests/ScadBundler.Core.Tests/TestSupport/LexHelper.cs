using ScadBundler.Core.Lexing;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>Convenience helpers for lexing inline source snippets in tests.</summary>
public static class LexHelper
{
    /// <summary>Lexes a snippet under the synthetic path <c>test.scad</c>.</summary>
    public static LexResult Lex(string source) => Lexer.Lex(new SourceFile("test.scad", source));

    /// <summary>Lexes a snippet and returns the full token stream (including EOF).</summary>
    public static IReadOnlyList<Token> Tokens(string source) => Lex(source).Tokens;

    /// <summary>Lexes a snippet and returns every token except the trailing EOF.</summary>
    public static IReadOnlyList<Token> Significant(string source) =>
        Lex(source).Tokens.Where(t => t.Kind != TokenKind.Eof).ToArray();

    /// <summary>Lexes a snippet and returns its single significant token (asserts there is exactly one).</summary>
    public static Token Single(string source)
    {
        IReadOnlyList<Token> tokens = Significant(source);
        Xunit.Assert.Single(tokens);
        return tokens[0];
    }
}
