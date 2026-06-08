using ScadBundler.Core;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Trivia propagation (Slice-2 §9): leading/blank-line from a node's first token, trailing from its
/// last token, and end-of-file comments preserved on <see cref="ScadFile"/>.
/// </summary>
public sealed class ParserTriviaTests
{
    private static CommentTrivia AsComment(Trivia trivia) => Assert.IsType<CommentTrivia>(trivia);

    [Fact]
    public void LeadingComment_AttachesToStatement()
    {
        Statement statement = ParseHelper.Single("// header\nx = 1;\n");
        Assert.Equal("// header", AsComment(Assert.Single(statement.LeadingTrivia)).Text);
    }

    [Fact] // AST-Reference §14.7
    public void CustomizerTrivia_LeadingSectionAndLabel_TrailingAnnotation()
    {
        Statement statement = ParseHelper.Single(
            "/* [Dimensions] */\n// Outer diameter of the part\ndiameter = 20; // [5:50]\n");

        Assert.Equal(2, statement.LeadingTrivia.Count);
        Assert.Equal("/* [Dimensions] */", AsComment(statement.LeadingTrivia[0]).Text);
        Assert.Equal("// Outer diameter of the part", AsComment(statement.LeadingTrivia[1]).Text);
        Assert.Equal("// [5:50]", AsComment(Assert.Single(statement.TrailingTrivia)).Text);
    }

    [Fact]
    public void TrailingAnnotation_RidesOnTheTerminatingSemicolon()
    {
        Statement statement = ParseHelper.Single("diameter = 20; // [5:50]\n");
        Assert.Equal("// [5:50]", AsComment(Assert.Single(statement.TrailingTrivia)).Text);
    }

    [Fact] // P-003
    public void BlankLineBefore_IsSetOnlyAfterABlankLine()
    {
        IReadOnlyList<Statement> statements = ParseHelper.Statements("a = 1;\n\nb = 2;\n");
        Assert.False(statements[0].BlankLineBefore);
        Assert.True(statements[1].BlankLineBefore);
    }

    [Fact]
    public void EndOfFileComment_IsPreservedOnTheRoot()
    {
        ScadFile root = ParseHelper.Parse("x = 1;\n// trailing license\n").Root;
        Assert.Equal("// trailing license", AsComment(Assert.Single(root.TrailingTrivia)).Text);
    }

    [Fact]
    public void OperatorExpressions_DoNotDuplicateTrivia()
    {
        // The leading comment belongs to the statement; the inner binary expression carries none,
        // so a comment never attaches to two nodes.
        Statement statement = ParseHelper.Single("// note\nx = a + b;\n");
        var assignment = Assert.IsType<AssignmentStatement>(statement);
        Assert.Single(statement.LeadingTrivia);
        Assert.Empty(assignment.Value.LeadingTrivia);
    }
}
