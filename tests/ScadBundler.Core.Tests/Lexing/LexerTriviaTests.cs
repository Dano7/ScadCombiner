using ScadBundler.Core;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Lexing;

public sealed class LexerTriviaTests
{
    private static Token First(IReadOnlyList<Token> tokens, TokenKind kind) =>
        tokens.First(t => t.Kind == kind);

    private static CommentTrivia AsComment(Trivia trivia) => Assert.IsType<CommentTrivia>(trivia);

    [Fact]
    public void LineComment_BecomesLeadingTriviaOfNextToken()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("// header\nx = 1;\n");
        Token x = First(tokens, TokenKind.Identifier);

        Trivia trivia = Assert.Single(x.LeadingTrivia);
        CommentTrivia comment = AsComment(trivia);
        Assert.Equal(CommentKind.Line, comment.Kind);
        Assert.Equal("// header", comment.Text);
        Assert.False(x.BlankLineBefore);
    }

    [Fact]
    public void BlockComment_BecomesLeadingTrivia_TextIncludesDelimiters()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("/* c */ x = 1;");
        Token x = First(tokens, TokenKind.Identifier);

        CommentTrivia comment = AsComment(Assert.Single(x.LeadingTrivia));
        Assert.Equal(CommentKind.Block, comment.Kind);
        Assert.Equal("/* c */", comment.Text);
    }

    [Fact]
    public void MultipleLeadingComments_ArePreservedInOrder()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("/* [Dimensions] */\n// label\ndiameter = 20;\n");
        Token diameter = First(tokens, TokenKind.Identifier);

        Assert.Equal(2, diameter.LeadingTrivia.Count);
        Assert.Equal("/* [Dimensions] */", AsComment(diameter.LeadingTrivia[0]).Text);
        Assert.Equal("// label", AsComment(diameter.LeadingTrivia[1]).Text);
    }

    [Fact]
    public void SameLineComment_BecomesTrailingTriviaOfPrecedingToken()
    {
        // Customizer inline annotation: the // [5:50] rides on the ';'.
        IReadOnlyList<Token> tokens = LexHelper.Tokens("diameter = 20; // [5:50]\n");
        Token semicolon = First(tokens, TokenKind.Semicolon);

        CommentTrivia comment = AsComment(Assert.Single(semicolon.TrailingTrivia));
        Assert.Equal("// [5:50]", comment.Text);
    }

    [Fact]
    public void BlankLineBefore_IsSetWhenABlankLineSeparatesStatements()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("a = 1;\n\nb = 2;\n");
        Token a = tokens[0];
        Token b = tokens[4];

        Assert.False(a.BlankLineBefore);
        Assert.True(b.BlankLineBefore);
    }

    [Fact]
    public void BlankLineBefore_IsFalseForAdjacentStatements()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("a = 1;\nb = 2;\n");
        Assert.False(tokens[4].BlankLineBefore);
    }

    [Fact]
    public void CommentLineBetweenStatements_DoesNotCountAsBlankLine()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("a = 1;\n// note\nb = 2;\n");
        Token b = tokens[4];

        Assert.False(b.BlankLineBefore);
        Assert.Equal("// note", AsComment(Assert.Single(b.LeadingTrivia)).Text);
    }

    [Fact]
    public void BlankLineBeforeACommentGroup_IsStillFlagged()
    {
        IReadOnlyList<Token> tokens = LexHelper.Significant("a = 1;\n\n// note\nb = 2;\n");
        Assert.True(tokens[4].BlankLineBefore);
    }

    [Fact]
    public void EndOfFileComment_AttachesToEof()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("x = 1;\n// trailing license\n");
        Token eof = tokens[^1];

        Assert.Equal(TokenKind.Eof, eof.Kind);
        Assert.Equal("// trailing license", AsComment(Assert.Single(eof.LeadingTrivia)).Text);
    }

    [Fact]
    public void BlockComment_SpanningLines_KeepsTextAndAdvancesPosition()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("/* a\nb */\nx = 1;");
        Token x = First(tokens, TokenKind.Identifier);

        CommentTrivia comment = AsComment(Assert.Single(x.LeadingTrivia));
        Assert.Equal("/* a\nb */", comment.Text);
        Assert.Equal(3, x.Span.Start.Line); // the comment consumed lines 1-2
    }

    [Fact]
    public void MultiLineBlockComment_StartingOnTokenLine_IsTrailing()
    {
        // A block comment that opens on the same line as ';' is trailing, even when it spans lines.
        IReadOnlyList<Token> tokens = LexHelper.Tokens("a = 1; /* x\ny */ b = 2;\n");
        Token firstSemicolon = First(tokens, TokenKind.Semicolon);

        CommentTrivia comment = AsComment(Assert.Single(firstSemicolon.TrailingTrivia));
        Assert.Equal(CommentKind.Block, comment.Kind);
        Assert.Equal("/* x\ny */", comment.Text);
    }

    [Fact]
    public void TrailingTrivia_OnlyCapturesSameLineComments()
    {
        IReadOnlyList<Token> tokens = LexHelper.Tokens("a = 1;\n// next line\nb = 2;\n");
        Token firstSemicolon = First(tokens, TokenKind.Semicolon);

        Assert.Empty(firstSemicolon.TrailingTrivia);
    }
}
