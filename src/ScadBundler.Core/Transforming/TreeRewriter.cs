using ScadBundler.Core.Ast;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// A rebuild-everything immutable rewriter base: walks the tree, rebuilding each node from its rewritten
/// children, and applies the overridable <see cref="Transform"/> hook to every rebuilt expression. Spans
/// and trivia survive (records are rebuilt with <c>with</c>). Subclasses that only replace certain
/// expression nodes (literal canonicalization, string decomposition, indirection) override
/// <see cref="Transform"/>; the recursion is shared here.
/// </summary>
internal abstract class TreeRewriter
{
    /// <summary>Rewrites a whole file.</summary>
    /// <param name="file">The file to rewrite.</param>
    /// <returns>The rewritten file.</returns>
    public ScadFile Rewrite(ScadFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file with { Statements = [.. file.Statements.Select(RewriteStatement)] };
    }

    /// <summary>Applied to every expression after its children are rewritten. Default: identity.</summary>
    /// <param name="rebuilt">The expression with rewritten children.</param>
    /// <returns>The replacement expression (or <paramref name="rebuilt"/> unchanged).</returns>
    protected virtual Expression Transform(Expression rebuilt) => rebuilt;

    protected Statement RewriteStatement(Statement statement) => statement switch
    {
        ModuleDefinition module => module with
        {
            Parameters = RewriteParameters(module.Parameters),
            Body = RewriteStatement(module.Body),
        },
        FunctionDefinition function => function with
        {
            Parameters = RewriteParameters(function.Parameters),
            Body = RewriteExpression(function.Body),
        },
        AssignmentStatement assignment => assignment with { Value = RewriteExpression(assignment.Value) },
        ModuleInstantiation instantiation => instantiation with
        {
            Arguments = RewriteArguments(instantiation.Arguments),
            Child = instantiation.Child is null ? null : RewriteStatement(instantiation.Child),
        },
        BlockStatement block => block with { Statements = [.. block.Statements.Select(RewriteStatement)] },
        IfStatement branch => branch with
        {
            Condition = RewriteExpression(branch.Condition),
            Then = RewriteStatement(branch.Then),
            Else = branch.Else is null ? null : RewriteStatement(branch.Else),
        },
        ForStatement loop => loop with { Bindings = RewriteBindings(loop.Bindings), Body = RewriteStatement(loop.Body) },
        IntersectionForStatement loop => loop with { Bindings = RewriteBindings(loop.Bindings), Body = RewriteStatement(loop.Body) },
        LetStatement let => let with { Bindings = RewriteBindings(let.Bindings), Body = RewriteStatement(let.Body) },
        _ => statement,
    };

    protected Expression RewriteExpression(Expression expression)
    {
        Expression rebuilt = expression switch
        {
            VectorExpression vector => vector with { Elements = [.. vector.Elements.Select(RewriteExpression)] },
            RangeExpression range => range with
            {
                Start = RewriteExpression(range.Start),
                Step = range.Step is null ? null : RewriteExpression(range.Step),
                End = RewriteExpression(range.End),
            },
            BinaryExpression binary => binary with { Left = RewriteExpression(binary.Left), Right = RewriteExpression(binary.Right) },
            UnaryExpression unary => unary with { Operand = RewriteExpression(unary.Operand) },
            ConditionalExpression conditional => conditional with
            {
                Condition = RewriteExpression(conditional.Condition),
                Then = RewriteExpression(conditional.Then),
                Else = RewriteExpression(conditional.Else),
            },
            ParenthesizedExpression parenthesized => parenthesized with { Inner = RewriteExpression(parenthesized.Inner) },
            IndexExpression index => index with { Target = RewriteExpression(index.Target), Index = RewriteExpression(index.Index) },
            MemberExpression member => member with { Target = RewriteExpression(member.Target) },
            FunctionCallExpression call => call with { Callee = RewriteExpression(call.Callee), Arguments = RewriteArguments(call.Arguments) },
            LetExpression let => let with { Bindings = RewriteBindings(let.Bindings), Body = RewriteExpression(let.Body) },
            AssertExpression assert => assert with
            {
                Arguments = RewriteArguments(assert.Arguments),
                Body = assert.Body is null ? null : RewriteExpression(assert.Body),
            },
            EchoExpression echo => echo with
            {
                Arguments = RewriteArguments(echo.Arguments),
                Body = echo.Body is null ? null : RewriteExpression(echo.Body),
            },
            FunctionLiteral literal => literal with { Parameters = RewriteParameters(literal.Parameters), Body = RewriteExpression(literal.Body) },
            ForComprehension comprehension => comprehension with { Bindings = RewriteBindings(comprehension.Bindings), Body = RewriteExpression(comprehension.Body) },
            ForCComprehension comprehension => comprehension with
            {
                Init = RewriteBindings(comprehension.Init),
                Condition = RewriteExpression(comprehension.Condition),
                Update = RewriteBindings(comprehension.Update),
                Body = RewriteExpression(comprehension.Body),
            },
            IfComprehension comprehension => comprehension with
            {
                Condition = RewriteExpression(comprehension.Condition),
                Then = RewriteExpression(comprehension.Then),
                Else = comprehension.Else is null ? null : RewriteExpression(comprehension.Else),
            },
            LetComprehension comprehension => comprehension with { Bindings = RewriteBindings(comprehension.Bindings), Body = RewriteExpression(comprehension.Body) },
            EachExpression each => each with { Value = RewriteExpression(each.Value) },
            _ => expression, // literals, identifiers
        };

        return Transform(rebuilt);
    }

    private List<Argument> RewriteArguments(IReadOnlyList<Argument> arguments) =>
        [.. arguments.Select(argument => argument with { Value = RewriteExpression(argument.Value) })];

    private List<Parameter> RewriteParameters(IReadOnlyList<Parameter> parameters) =>
        [.. parameters.Select(parameter => parameter.DefaultValue is null
            ? parameter
            : parameter with { DefaultValue = RewriteExpression(parameter.DefaultValue) })];

    private List<Binding> RewriteBindings(IReadOnlyList<Binding> bindings) =>
        [.. bindings.Select(binding => binding with { Value = RewriteExpression(binding.Value) })];
}
