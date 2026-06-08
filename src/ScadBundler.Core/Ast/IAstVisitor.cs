namespace ScadBundler.Core.Ast;

/// <summary>
/// A generic visitor with one <c>Visit</c> overload per concrete AST node. Because the hierarchy is
/// closed and sealed, the overload set is exhaustive. Trivia is not visited.
/// </summary>
/// <typeparam name="TResult">The result produced for each visited node.</typeparam>
public interface IAstVisitor<out TResult>
{
    /// <summary>Visits a <see cref="ScadFile"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ScadFile node);

    /// <summary>Visits an <see cref="IncludeStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(IncludeStatement node);

    /// <summary>Visits a <see cref="UseStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(UseStatement node);

    /// <summary>Visits a <see cref="ModuleDefinition"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ModuleDefinition node);

    /// <summary>Visits a <see cref="FunctionDefinition"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(FunctionDefinition node);

    /// <summary>Visits an <see cref="AssignmentStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(AssignmentStatement node);

    /// <summary>Visits a <see cref="ModuleInstantiation"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ModuleInstantiation node);

    /// <summary>Visits a <see cref="BlockStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(BlockStatement node);

    /// <summary>Visits an <see cref="IfStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(IfStatement node);

    /// <summary>Visits a <see cref="ForStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ForStatement node);

    /// <summary>Visits an <see cref="IntersectionForStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(IntersectionForStatement node);

    /// <summary>Visits a <see cref="LetStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(LetStatement node);

    /// <summary>Visits an <see cref="EmptyStatement"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(EmptyStatement node);

    /// <summary>Visits a <see cref="NumberLiteral"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(NumberLiteral node);

    /// <summary>Visits a <see cref="StringLiteral"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(StringLiteral node);

    /// <summary>Visits a <see cref="BooleanLiteral"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(BooleanLiteral node);

    /// <summary>Visits an <see cref="UndefLiteral"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(UndefLiteral node);

    /// <summary>Visits an <see cref="Identifier"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(Identifier node);

    /// <summary>Visits a <see cref="VectorExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(VectorExpression node);

    /// <summary>Visits a <see cref="RangeExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(RangeExpression node);

    /// <summary>Visits a <see cref="BinaryExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(BinaryExpression node);

    /// <summary>Visits a <see cref="UnaryExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(UnaryExpression node);

    /// <summary>Visits a <see cref="ConditionalExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ConditionalExpression node);

    /// <summary>Visits a <see cref="ParenthesizedExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ParenthesizedExpression node);

    /// <summary>Visits an <see cref="IndexExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(IndexExpression node);

    /// <summary>Visits a <see cref="MemberExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(MemberExpression node);

    /// <summary>Visits a <see cref="FunctionCallExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(FunctionCallExpression node);

    /// <summary>Visits a <see cref="LetExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(LetExpression node);

    /// <summary>Visits an <see cref="AssertExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(AssertExpression node);

    /// <summary>Visits an <see cref="EchoExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(EchoExpression node);

    /// <summary>Visits a <see cref="FunctionLiteral"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(FunctionLiteral node);

    /// <summary>Visits a <see cref="ForComprehension"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ForComprehension node);

    /// <summary>Visits a <see cref="ForCComprehension"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(ForCComprehension node);

    /// <summary>Visits an <see cref="IfComprehension"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(IfComprehension node);

    /// <summary>Visits a <see cref="LetComprehension"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(LetComprehension node);

    /// <summary>Visits an <see cref="EachExpression"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(EachExpression node);

    /// <summary>Visits a <see cref="Parameter"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(Parameter node);

    /// <summary>Visits an <see cref="Argument"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(Argument node);

    /// <summary>Visits a <see cref="Binding"/>.</summary>
    /// <param name="node">The node to visit.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit(Binding node);
}
