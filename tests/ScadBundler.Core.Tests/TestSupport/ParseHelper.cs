using ScadBundler.Core.Ast;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Text;
using Xunit;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>Convenience helpers for parsing inline source snippets in tests.</summary>
public static class ParseHelper
{
    /// <summary>Lexes then parses a snippet under the synthetic path <c>test.scad</c> (diagnostics merged).</summary>
    /// <param name="source">The source snippet.</param>
    /// <returns>The parse result.</returns>
    public static ParseResult Parse(string source) =>
        Parser.Parse(new SourceFile("test.scad", source));

    /// <summary>Parses a snippet and returns only the parser's diagnostics (lexer diagnostics excluded).</summary>
    /// <param name="source">The source snippet.</param>
    /// <returns>The parse result with parser-only diagnostics.</returns>
    public static ParseResult ParseTokensOnly(string source)
    {
        var file = new SourceFile("test.scad", source);
        LexResult lex = Lexer.Lex(file);
        return Parser.Parse(file, lex.Tokens);
    }

    /// <summary>Parses a snippet and returns its top-level statements.</summary>
    /// <param name="source">The source snippet.</param>
    /// <returns>The parsed statements.</returns>
    public static IReadOnlyList<Statement> Statements(string source) => Parse(source).Root.Statements;

    /// <summary>Parses a snippet and returns its single top-level statement (asserts there is exactly one).</summary>
    /// <param name="source">The source snippet.</param>
    /// <returns>The single statement.</returns>
    public static Statement Single(string source)
    {
        IReadOnlyList<Statement> statements = Statements(source);
        Assert.Single(statements);
        return statements[0];
    }

    /// <summary>Parses a bare expression by wrapping it in an assignment and returning the value.</summary>
    /// <param name="expression">The expression source (without trailing semicolon).</param>
    /// <returns>The parsed value expression.</returns>
    public static Expression Expression(string expression)
    {
        Statement statement = Single($"__v = {expression};");
        var assignment = Assert.IsType<AssignmentStatement>(statement);
        return assignment.Value;
    }
}
