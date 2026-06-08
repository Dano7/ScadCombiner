using ScadBundler.Core.Text;

namespace ScadBundler.Core.Ast;

/// <summary>
/// The parsed contents of one <c>.scad</c> file, or the final bundled output: a sequence of
/// top-level statements. Any comment trivia attached to the end-of-file token is preserved on
/// <see cref="AstNode.TrailingTrivia"/> so trailing license/section comments are not dropped.
/// </summary>
/// <param name="Source">The source file these statements were parsed from.</param>
/// <param name="Statements">The top-level statements, in source order.</param>
public sealed record ScadFile(
    SourceFile Source,
    IReadOnlyList<Statement> Statements) : AstNode
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}
