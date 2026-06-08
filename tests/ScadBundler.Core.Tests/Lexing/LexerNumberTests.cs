using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Lexing;

public sealed class LexerNumberTests
{
    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("42", 42.0)]
    [InlineData("1.0", 1.0)]
    [InlineData(".5", 0.5)]
    [InlineData("1.", 1.0)]
    [InlineData("1e3", 1000.0)]
    [InlineData("1.5e-3", 0.0015)]
    [InlineData("1.e10", 1e10)]
    [InlineData("2.5e3", 2500.0)]
    [InlineData("0xFF", 255.0)]
    [InlineData("0xdeadBEEF", 3735928559.0)]
    public void Numbers_DecodeValueAndPreserveRawText(string raw, double expected)
    {
        LexResult result = LexHelper.Lex(raw);
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.Number, token.Kind);
        Assert.Equal(raw, token.Text);
        Assert.NotNull(token.NumberValue);
        Assert.Equal(expected, token.NumberValue!.Value, 9);
        Assert.Null(token.StringValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void HexPrefix_IsLowercaseOnly()
    {
        // Uppercase 'X' is not a hex prefix; maximal munch makes "0X10" one digit-leading identifier.
        LexResult result = LexHelper.Lex("0X10");
        Token token = result.Tokens[0];
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal("0X10", token.Text);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DigitLeadingIdentifier);
    }

    [Fact]
    public void DigitLeadingIdentifier_WinsByMaximalMunch_AndWarns()
    {
        LexResult result = LexHelper.Lex("2d");
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal("2d", token.Text);
        Diagnostic diag = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.DigitLeadingIdentifier, diag.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }

    [Fact]
    public void NumberFollowedByDot_StopsAtTrailingDecimal()
    {
        // "2.x": "2." is a valid number ({D}+\.{D}*), then identifier "x".
        IReadOnlyList<Token> tokens = LexHelper.Significant("2.x");
        Assert.Equal(TokenKind.Number, tokens[0].Kind);
        Assert.Equal("2.", tokens[0].Text);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("x", tokens[1].Text);
    }

    [Fact]
    public void BareDot_IsNotANumber()
    {
        Token token = LexHelper.Single(".");
        Assert.Equal(TokenKind.Dot, token.Kind);
    }

    [Fact]
    public void IncompleteExponent_FallsBackToDigitLeadingIdentifier()
    {
        // "1e" has no exponent digits, so it is a (deprecated) digit-leading identifier.
        LexResult result = LexHelper.Lex("1e");
        Assert.Equal(TokenKind.Identifier, result.Tokens[0].Kind);
        Assert.Equal("1e", result.Tokens[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DigitLeadingIdentifier);
    }

    [Theory]
    [InlineData("123456789012345678901234567890")] // overflows 64-bit
    [InlineData("9007199254740993")]                // 2^53 + 1, not exactly representable
    [InlineData("0xFFFFFFFFFFFFFFFFF")]             // 17 hex digits, overflows 64-bit
    public void OversizedIntegers_WarnButStillProduceANumber(string raw)
    {
        LexResult result = LexHelper.Lex(raw);
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.Number, token.Kind);
        Assert.Equal(raw, token.Text);
        Assert.NotNull(token.NumberValue);
        Diagnostic diag = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ImpreciseNumber, diag.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }

    [Fact]
    public void ExactlyRepresentableLargeInteger_DoesNotWarn()
    {
        LexResult result = LexHelper.Lex("9007199254740992"); // 2^53, exactly representable
        Assert.Equal(TokenKind.Number, result.Tokens[0].Kind);
        Assert.Empty(result.Diagnostics);
    }
}
