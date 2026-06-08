using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Lexing;

public sealed class LexerStringTests
{
    // OpenSCAD source building blocks, kept as char constants so this file stays pure ASCII.
    private const char BS = '\\'; // a backslash in the .scad source
    private const char Q = '"';   // a double quote in the .scad source

    private static string Quoted(string inner) => Q + inner + Q;

    [Fact]
    public void EmptyString_DecodesToEmpty_RawKeepsQuotes()
    {
        LexResult result = LexHelper.Lex(Quoted(string.Empty));
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.String, token.Kind);
        Assert.Equal("\"\"", token.Text);
        Assert.Equal(string.Empty, token.StringValue);
        Assert.Null(token.NumberValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void StandardEscapes_AreDecoded_RawIsPreserved()
    {
        // .scad source:  "\n\t\r\\\""
        string inner = BS + "n" + BS + "t" + BS + "r" + BS.ToString() + BS + BS + Q;
        string source = Quoted(inner);

        LexResult result = LexHelper.Lex(source);
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.String, token.Kind);
        Assert.Equal("\n\t\r" + BS + Q, token.StringValue);
        Assert.Equal(source, token.Text);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void HexEscape_DecodesByte()
    {
        Token token = LexHelper.Single(Quoted(BS + "x41"));
        Assert.Equal("A", token.StringValue);
    }

    [Fact]
    public void HexEscapeZero_MapsToSpace()
    {
        // OpenSCAD maps \x00 to a space rather than a NUL.
        Token token = LexHelper.Single(Quoted(BS + "x00"));
        Assert.Equal(" ", token.StringValue);
    }

    [Fact]
    public void HexEscape_RequiresLeadingOctalDigit_OtherwiseUndefined()
    {
        // \x with a non-[0-7] first digit is an undefined escape (\x), then 'F','F' as literals.
        LexResult result = LexHelper.Lex(Quoted(BS + "xFF"));
        Token token = result.Tokens[0];
        Assert.Equal("xFF", token.StringValue);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedEscape);
    }

    [Fact]
    public void UnicodeEscape_FourHex_Decodes()
    {
        Token token = LexHelper.Single(Quoted(BS + "u0041"));
        Assert.Equal("A", token.StringValue);
    }

    [Fact]
    public void UnicodeEscape_SixHex_DecodesAstralPlane()
    {
        Token token = LexHelper.Single(Quoted(BS + "U01F600"));
        Assert.Equal(char.ConvertFromUtf32(0x1F600), token.StringValue);
    }

    [Fact]
    public void NonAsciiContent_IsAllowedInsideStrings()
    {
        // "é" — non-ASCII is fine inside a string and produces no diagnostic.
        string source = Quoted(((char)0x00E9).ToString());
        LexResult result = LexHelper.Lex(source);
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.String, token.Kind);
        Assert.Equal(((char)0x00E9).ToString(), token.StringValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UndefinedEscape_DropsBackslashKeepsChar_AndWarns()
    {
        LexResult result = LexHelper.Lex(Quoted(BS + "z"));
        Token token = result.Tokens[0];

        Assert.Equal("z", token.StringValue);
        Diagnostic diag = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.UndefinedEscape, diag.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }

    [Fact]
    public void UnterminatedString_AtEof_RecoversAndReports()
    {
        LexResult result = LexHelper.Lex(Q + "abc");
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.String, token.Kind);
        Assert.Equal("abc", token.StringValue);
        Assert.Equal(Q + "abc", token.Text);
        Diagnostic diag = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.UnterminatedString, diag.Code);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(1, diag.Span.Start.Column); // points at the opening quote
    }

    [Fact]
    public void UnterminatedString_AtNewline_RecoversAtLineBoundary()
    {
        LexResult result = LexHelper.Lex(Q + "abc\nx = 1;\n");

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnterminatedString);
        // Recovery continues on the next line: the assignment is still tokenized.
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.Assign);
    }

    [Fact]
    public void StringEndingWithBackslashAtEof_RecoversAsUnterminated()
    {
        LexResult result = LexHelper.Lex(Q + "abc" + BS);
        Token token = result.Tokens[0];

        Assert.Equal(TokenKind.String, token.Kind);
        Assert.Equal("abc", token.StringValue);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnterminatedString);
    }

    [Fact]
    public void InvalidUnicodeCodePoint_BecomesReplacementChar()
    {
        // \uD800 is a lone surrogate (not a valid scalar value) -> U+FFFD replacement.
        Token token = LexHelper.Single(Quoted(BS + "uD800"));
        Assert.Equal(((char)0xFFFD).ToString(), token.StringValue);
    }

    [Fact]
    public void RawText_DiffersFromDecodedValue()
    {
        string source = Quoted(BS + "n"); // the 4 chars:  "  \  n  "
        Token token = LexHelper.Single(source);
        Assert.Equal("\n", token.StringValue); // decoded: one LF
        Assert.Equal(source, token.Text);      // raw lexeme preserved verbatim
        Assert.Equal(4, token.Text.Length);
    }
}
