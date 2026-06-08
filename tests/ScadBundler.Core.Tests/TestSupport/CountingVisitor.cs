using ScadBundler.Core.Ast;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// A complete <see cref="IAstVisitor{TResult}"/> implementation that walks the whole tree, recording
/// each visited node's type name and returning the number of nodes in the visited subtree. Used to
/// validate that every concrete node's <c>Accept</c> dispatches to the matching <c>Visit</c> overload.
/// </summary>
public sealed class CountingVisitor : IAstVisitor<int>
{
    /// <summary>The concrete type name of every node visited (children before parent / post-order).</summary>
    public List<string> Visited { get; } = [];

    private int Node(AstNode node, params int[] childCounts)
    {
        Visited.Add(node.GetType().Name);
        int total = 1;
        foreach (int count in childCounts)
        {
            total += count;
        }

        return total;
    }

    private int Each(IEnumerable<AstNode> nodes)
    {
        int total = 0;
        foreach (AstNode node in nodes)
        {
            total += node.Accept(this);
        }

        return total;
    }

    private int Opt(AstNode? node) => node?.Accept(this) ?? 0;

    /// <inheritdoc/>
    public int Visit(ScadFile node) => Node(node, Each(node.Statements));

    /// <inheritdoc/>
    public int Visit(IncludeStatement node) => Node(node);

    /// <inheritdoc/>
    public int Visit(UseStatement node) => Node(node);

    /// <inheritdoc/>
    public int Visit(ModuleDefinition node) => Node(node, Each(node.Parameters), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(FunctionDefinition node) => Node(node, Each(node.Parameters), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(AssignmentStatement node) => Node(node, node.Value.Accept(this));

    /// <inheritdoc/>
    public int Visit(ModuleInstantiation node) => Node(node, Each(node.Arguments), Opt(node.Child));

    /// <inheritdoc/>
    public int Visit(BlockStatement node) => Node(node, Each(node.Statements));

    /// <inheritdoc/>
    public int Visit(IfStatement node) => Node(node, node.Condition.Accept(this), node.Then.Accept(this), Opt(node.Else));

    /// <inheritdoc/>
    public int Visit(ForStatement node) => Node(node, Each(node.Bindings), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(IntersectionForStatement node) => Node(node, Each(node.Bindings), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(LetStatement node) => Node(node, Each(node.Bindings), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(EmptyStatement node) => Node(node);

    /// <inheritdoc/>
    public int Visit(NumberLiteral node) => Node(node);

    /// <inheritdoc/>
    public int Visit(StringLiteral node) => Node(node);

    /// <inheritdoc/>
    public int Visit(BooleanLiteral node) => Node(node);

    /// <inheritdoc/>
    public int Visit(UndefLiteral node) => Node(node);

    /// <inheritdoc/>
    public int Visit(Identifier node) => Node(node);

    /// <inheritdoc/>
    public int Visit(VectorExpression node) => Node(node, Each(node.Elements));

    /// <inheritdoc/>
    public int Visit(RangeExpression node) => Node(node, node.Start.Accept(this), Opt(node.Step), node.End.Accept(this));

    /// <inheritdoc/>
    public int Visit(BinaryExpression node) => Node(node, node.Left.Accept(this), node.Right.Accept(this));

    /// <inheritdoc/>
    public int Visit(UnaryExpression node) => Node(node, node.Operand.Accept(this));

    /// <inheritdoc/>
    public int Visit(ConditionalExpression node) => Node(node, node.Condition.Accept(this), node.Then.Accept(this), node.Else.Accept(this));

    /// <inheritdoc/>
    public int Visit(ParenthesizedExpression node) => Node(node, node.Inner.Accept(this));

    /// <inheritdoc/>
    public int Visit(IndexExpression node) => Node(node, node.Target.Accept(this), node.Index.Accept(this));

    /// <inheritdoc/>
    public int Visit(MemberExpression node) => Node(node, node.Target.Accept(this));

    /// <inheritdoc/>
    public int Visit(FunctionCallExpression node) => Node(node, node.Callee.Accept(this), Each(node.Arguments));

    /// <inheritdoc/>
    public int Visit(LetExpression node) => Node(node, Each(node.Bindings), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(AssertExpression node) => Node(node, Each(node.Arguments), Opt(node.Body));

    /// <inheritdoc/>
    public int Visit(EchoExpression node) => Node(node, Each(node.Arguments), Opt(node.Body));

    /// <inheritdoc/>
    public int Visit(FunctionLiteral node) => Node(node, Each(node.Parameters), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(ForComprehension node) => Node(node, Each(node.Bindings), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(ForCComprehension node) =>
        Node(node, Each(node.Init), node.Condition.Accept(this), Each(node.Update), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(IfComprehension node) => Node(node, node.Condition.Accept(this), node.Then.Accept(this), Opt(node.Else));

    /// <inheritdoc/>
    public int Visit(LetComprehension node) => Node(node, Each(node.Bindings), node.Body.Accept(this));

    /// <inheritdoc/>
    public int Visit(EachExpression node) => Node(node, node.Value.Accept(this));

    /// <inheritdoc/>
    public int Visit(Parameter node) => Node(node, Opt(node.DefaultValue));

    /// <inheritdoc/>
    public int Visit(Argument node) => Node(node, node.Value.Accept(this));

    /// <inheritdoc/>
    public int Visit(Binding node) => Node(node, node.Value.Accept(this));
}
