using ScadBundler.Core.Lexing;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Lexing;

public sealed class LexerTests
{
    private static void AssertKinds(string source, params TokenKind[] expected)
    {
        TokenKind[] actual = LexHelper.Significant(source).Select(t => t.Kind).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptyInput_ProducesOnlyEof()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens(string.Empty);
        Token eof = Assert.Single(tokens);
        Assert.Equal(TokenKind.Eof, eof.Kind);
        Assert.Equal(1, eof.Span.Start.Line);
        Assert.Equal(1, eof.Span.Start.Column);
    }

    [Fact]
    public void TokenStream_AlwaysEndsWithSingleEof()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("a = 1; b = 2;");
        Assert.Equal(TokenKind.Eof, tokens[^1].Kind);
        Assert.Single(tokens, t => t.Kind == TokenKind.Eof);
    }

    [Fact]
    public void EveryTokenKind_IsProducedBySomeInput()
    {
        var produced = new HashSet<TokenKind>();
        string[] sources =
        [
            "include <a.scad>",
            "use <b.scad>",
            "module function if else for let assert echo each",
            "true false undef",
            "ident \"str\" 42",
            "= ( ) { } [ ] ; , : . ? + - * / % ^",
            "< <= > >= == != && || ! & | ~ << >> #",
        ];

        foreach (string source in sources)
        {
            foreach (Token token in LexHelper.Tokens(source))
            {
                produced.Add(token.Kind);
            }
        }

        foreach (TokenKind kind in Enum.GetValues<TokenKind>())
        {
            Assert.Contains(kind, produced);
        }
    }

    [Fact]
    public void Punctuation_LexesToExpectedKinds()
    {
        AssertKinds(
            "( ) [ ] { } ; , : . ? =",
            TokenKind.LParen, TokenKind.RParen, TokenKind.LBracket, TokenKind.RBracket,
            TokenKind.LBrace, TokenKind.RBrace, TokenKind.Semicolon, TokenKind.Comma,
            TokenKind.Colon, TokenKind.Dot, TokenKind.Question, TokenKind.Assign);
    }

    [Fact]
    public void Operators_LexToExpectedKinds()
    {
        AssertKinds(
            "+ - * / % ^ ! ~ & | #",
            TokenKind.Plus, TokenKind.Minus, TokenKind.Star, TokenKind.Slash, TokenKind.Percent,
            TokenKind.Caret, TokenKind.Not, TokenKind.Tilde, TokenKind.Amp, TokenKind.Pipe, TokenKind.Hash);
    }

    [Fact]
    public void MultiCharOperators_PreferMaximalMunch()
    {
        AssertKinds(
            "<= >= == != && || << >>",
            TokenKind.LessEqual, TokenKind.GreaterEqual, TokenKind.Equal, TokenKind.NotEqual,
            TokenKind.And, TokenKind.Or, TokenKind.ShiftLeft, TokenKind.ShiftRight);
    }

    [Fact]
    public void SingleCharRelationalAndBitwise_DisambiguateFromTwoChar()
    {
        AssertKinds(
            "< > = ! & |",
            TokenKind.Less, TokenKind.Greater, TokenKind.Assign, TokenKind.Not,
            TokenKind.Amp, TokenKind.Pipe);
    }

    [Fact]
    public void Keywords_LexToTheirKinds()
    {
        AssertKinds(
            "module function if else for let assert echo each true false undef",
            TokenKind.Module, TokenKind.Function, TokenKind.If, TokenKind.Else, TokenKind.For,
            TokenKind.Let, TokenKind.Assert, TokenKind.Echo, TokenKind.Each, TokenKind.True,
            TokenKind.False, TokenKind.Undef);
    }

    [Theory]
    [InlineData("intersection_for")]
    [InlineData("cube")]
    [InlineData("translate")]
    [InlineData("union")]
    [InlineData("children")]
    [InlineData("sphere")]
    public void BuiltinAndPseudoKeywordNames_AreIdentifiers(string name)
    {
        Token token = LexHelper.Single(name);
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(name, token.Text);
    }

    [Theory]
    [InlineData("$fn")]
    [InlineData("$fa")]
    [InlineData("$t")]
    [InlineData("$children")]
    [InlineData("$preview")]
    [InlineData("$")]
    public void SpecialVariables_AreIdentifiersIncludingDollar(string name)
    {
        Token token = LexHelper.Single(name);
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(name, token.Text);
    }

    [Theory]
    [InlineData("_x")]
    [InlineData("abc123")]
    [InlineData("_")]
    [InlineData("Camel_Case99")]
    public void Identifiers_AcceptLetterUnderscoreDigitTails(string name)
    {
        Token token = LexHelper.Single(name);
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(name, token.Text);
    }

    [Fact]
    public void TokenSpans_TrackLineAndColumnAcrossNewlines()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("a = 1;\nbb = 2;\n");

        // a=1; on line 1
        AssertAt(tokens[0], TokenKind.Identifier, 1, 1);
        AssertAt(tokens[1], TokenKind.Assign, 1, 3);
        AssertAt(tokens[2], TokenKind.Number, 1, 5);
        AssertAt(tokens[3], TokenKind.Semicolon, 1, 6);
        // bb=2; on line 2
        AssertAt(tokens[4], TokenKind.Identifier, 2, 1);
        AssertAt(tokens[5], TokenKind.Assign, 2, 4);
        AssertAt(tokens[6], TokenKind.Number, 2, 6);
        AssertAt(tokens[7], TokenKind.Semicolon, 2, 7);
    }

    [Fact]
    public void CarriageReturnsDoNotAdvanceColumns()
    {
        // CRLF line endings must yield the same columns as bare LF.
        IReadOnlyList<Token> tokens = LexHelper.Significant("a = 1;\r\nb = 2;\r\n");
        AssertAt(tokens[4], TokenKind.Identifier, 2, 1);
    }

    [Fact]
    public void NoBreakSpaceAndBom_AreTreatedAsWhitespace()
    {
        // Built from char codes to keep this source file pure ASCII:
        // U+FEFF (BOM) a  U+00A0 (NO-BREAK SPACE) b
        string source = (char)0xFEFF + "a" + (char)0x00A0 + "b";
        AssertKinds(source, TokenKind.Identifier, TokenKind.Identifier);
    }

    private static void AssertAt(Token token, TokenKind kind, int line, int column)
    {
        Assert.Equal(kind, token.Kind);
        Assert.Equal(line, token.Span.Start.Line);
        Assert.Equal(column, token.Span.Start.Column);
    }
}
