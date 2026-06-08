using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Lexing;

public sealed class LexerDiagnosticTests
{
    private static void AssertKinds(IReadOnlyList<Token> tokens, params TokenKind[] expected) =>
        Assert.Equal(expected, tokens.Select(t => t.Kind).ToArray());

    // ----- include / use ------------------------------------------------------------------------

    [Theory]
    [InlineData("include <a.scad>", "a.scad")]
    [InlineData("include<a.scad>", "a.scad")]
    [InlineData("use <MCAD/gears.scad>", "MCAD/gears.scad")]
    [InlineData("use <std.scad>", "std.scad")]
    public void IncludeUse_WithAnglePath_ProducesKeywordAndFilePath(string source, string expectedPath)
    {
        LexResult result = LexHelper.Lex(source);
        Token[] significant = result.Tokens.Where(t => t.Kind != TokenKind.Eof).ToArray();

        Assert.Equal(2, significant.Length);
        Assert.True(significant[0].Kind is TokenKind.Include or TokenKind.Use);
        Assert.Equal(TokenKind.FilePath, significant[1].Kind);
        Assert.Equal(expectedPath, significant[1].Text);
        Assert.Equal(expectedPath, significant[1].StringValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Include_AllowsWhitespaceAndNewlineBeforeAngle()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("include\n  <a.scad>");
        AssertKinds(tokens, TokenKind.Include, TokenKind.FilePath);
        Assert.Equal("a.scad", tokens[1].Text);
    }

    [Fact]
    public void Include_NotFollowedByAngle_IsAnIdentifier()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("include = 5;");
        AssertKinds(tokens, TokenKind.Identifier, TokenKind.Assign, TokenKind.Number, TokenKind.Semicolon);
        Assert.Equal("include", tokens[0].Text);
    }

    [Fact]
    public void Use_NotFollowedByAngle_IsAnIdentifier()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("use + 1");
        AssertKinds(tokens, TokenKind.Identifier, TokenKind.Plus, TokenKind.Number);
        Assert.Equal("use", tokens[0].Text);
    }

    [Fact]
    public void UnterminatedInclude_ReportsSb1003_AndRecovers()
    {
        LexResult result = LexHelper.Lex("include <a.scad");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnterminatedIncludeUse);
        Assert.Equal(DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
        Assert.Equal(TokenKind.Eof, result.Tokens[^1].Kind);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.FilePath);
    }

    [Fact]
    public void NewlineInIncludePath_ReportsSb1009_AndKeepsScanning()
    {
        LexResult result = LexHelper.Lex("include <a\nb.scad>");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.NewlineInIncludePath);
        Assert.Equal(DiagnosticSeverity.Warning, result.Diagnostics[0].Severity);

        Token filePath = result.Tokens.First(t => t.Kind == TokenKind.FilePath);
        Assert.Equal("ab.scad", filePath.Text); // the newline is dropped from the path
    }

    // ----- block comments, stray characters -----------------------------------------------------

    [Fact]
    public void UnterminatedBlockComment_ReportsSb1002()
    {
        LexResult result = LexHelper.Lex("/* never closed");
        Diagnostic diag = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.UnterminatedBlockComment, diag.Code);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(TokenKind.Eof, result.Tokens[^1].Kind);
    }

    [Fact]
    public void UnexpectedCharacter_ReportsSb1004_AndRecovers()
    {
        LexResult result = LexHelper.Lex("a @ b");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnexpectedCharacter);
        // Recovery: the surrounding identifiers are still tokenized.
        Assert.Equal(2, result.Tokens.Count(t => t.Kind == TokenKind.Identifier));
    }

    [Fact]
    public void NonAsciiOutsideStringOrComment_ReportsSb1005_AndRecovers()
    {
        // a <U+00E9> b
        string source = "a " + (char)0x00E9 + " b";
        LexResult result = LexHelper.Lex(source);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.NonAsciiCharacter);
        Assert.Equal(2, result.Tokens.Count(t => t.Kind == TokenKind.Identifier));
    }

    [Fact]
    public void Recovery_CollectsAllErrors_AndStillTerminatesWithEof()
    {
        // U+00E9 (non-ASCII), '@' (unexpected), then an unterminated string.
        string source = (char)0x00E9 + "@" + "\"abc";
        LexResult result = LexHelper.Lex(source);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.NonAsciiCharacter);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnexpectedCharacter);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnterminatedString);
        Assert.Equal(TokenKind.Eof, result.Tokens[^1].Kind);
    }
}
