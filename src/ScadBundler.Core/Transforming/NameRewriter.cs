using ScadBundler.Core.Ast;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// A pure rename pass: rebuilds an immutable subtree applying a <c>node → new name</c> map keyed by
/// reference identity (AST-Reference §15.6). Declaration nodes (module/function/assignment) and
/// reference nodes (<see cref="Identifier"/>, <see cref="ModuleInstantiation"/>) whose identity is in
/// the map get the new name; every other node is rebuilt unchanged so spans/trivia survive. Unlike the
/// inliner's <c>BundleRewriter</c> this does <b>no</b> deprecated-construct normalization (the bundle is
/// already normalized) — it only renames, so a hardening pass can re-run it without re-emitting SB500x.
/// </summary>
internal sealed class NameRewriter
{
    private readonly IReadOnlyDictionary<AstNode, string> _renames;

    public NameRewriter(IReadOnlyDictionary<AstNode, string> renames) => _renames = renames;

    public ScadFile Rewrite(ScadFile file) =>
        file with { Statements = [.. file.Statements.Select(RewriteStatement)] };

    public Statement RewriteStatement(Statement statement) => statement switch
    {
        ModuleDefinition module => module with
        {
            Name = Renamed(module, module.Name),
            Parameters = RewriteParameters(module.Parameters),
            Body = RewriteStatement(module.Body),
        },
        FunctionDefinition function => function with
        {
            Name = Renamed(function, function.Name),
            Parameters = RewriteParameters(function.Parameters),
            Body = RewriteExpression(function.Body),
        },
        AssignmentStatement assignment => assignment with
        {
            Name = Renamed(assignment, assignment.Name),
            Value = RewriteExpression(assignment.Value),
        },
        ModuleInstantiation instantiation => instantiation with
        {
            Name = Renamed(instantiation, instantiation.Name),
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
        ForStatement loop => loop with
        {
            Bindings = RewriteBindings(loop.Bindings),
            Body = RewriteStatement(loop.Body),
        },
        IntersectionForStatement loop => loop with
        {
            Bindings = RewriteBindings(loop.Bindings),
            Body = RewriteStatement(loop.Body),
        },
        LetStatement let => let with
        {
            Bindings = RewriteBindings(let.Bindings),
            Body = RewriteStatement(let.Body),
        },
        _ => statement, // IncludeStatement / UseStatement / EmptyStatement: no inner references
    };

    public Expression RewriteExpression(Expression expression) => expression switch
    {
        Identifier identifier => identifier with { Name = Renamed(identifier, identifier.Name) },
        NumberLiteral or StringLiteral or BooleanLiteral or UndefLiteral => expression,
        VectorExpression vector => vector with { Elements = [.. vector.Elements.Select(RewriteExpression)] },
        RangeExpression range => range with
        {
            Start = RewriteExpression(range.Start),
            Step = range.Step is null ? null : RewriteExpression(range.Step),
            End = RewriteExpression(range.End),
        },
        BinaryExpression binary => binary with
        {
            Left = RewriteExpression(binary.Left),
            Right = RewriteExpression(binary.Right),
        },
        UnaryExpression unary => unary with { Operand = RewriteExpression(unary.Operand) },
        ConditionalExpression conditional => conditional with
        {
            Condition = RewriteExpression(conditional.Condition),
            Then = RewriteExpression(conditional.Then),
            Else = RewriteExpression(conditional.Else),
        },
        ParenthesizedExpression parenthesized => parenthesized with { Inner = RewriteExpression(parenthesized.Inner) },
        IndexExpression index => index with
        {
            Target = RewriteExpression(index.Target),
            Index = RewriteExpression(index.Index),
        },
        MemberExpression member => member with { Target = RewriteExpression(member.Target) },
        FunctionCallExpression call => call with
        {
            Callee = RewriteExpression(call.Callee),
            Arguments = RewriteArguments(call.Arguments),
        },
        LetExpression let => let with
        {
            Bindings = RewriteBindings(let.Bindings),
            Body = RewriteExpression(let.Body),
        },
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
        FunctionLiteral literal => literal with
        {
            Parameters = RewriteParameters(literal.Parameters),
            Body = RewriteExpression(literal.Body),
        },
        ForComprehension comprehension => comprehension with
        {
            Bindings = RewriteBindings(comprehension.Bindings),
            Body = RewriteExpression(comprehension.Body),
        },
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
        LetComprehension comprehension => comprehension with
        {
            Bindings = RewriteBindings(comprehension.Bindings),
            Body = RewriteExpression(comprehension.Body),
        },
        EachExpression each => each with { Value = RewriteExpression(each.Value) },
        _ => expression,
    };

    private List<Argument> RewriteArguments(IReadOnlyList<Argument> arguments) =>
        [.. arguments.Select(argument => argument with { Value = RewriteExpression(argument.Value) })];

    private List<Parameter> RewriteParameters(IReadOnlyList<Parameter> parameters) =>
        [.. parameters.Select(parameter => parameter.DefaultValue is null
            ? parameter
            : parameter with { DefaultValue = RewriteExpression(parameter.DefaultValue) })];

    private List<Binding> RewriteBindings(IReadOnlyList<Binding> bindings) =>
        [.. bindings.Select(binding => binding with { Value = RewriteExpression(binding.Value) })];

    private string Renamed(AstNode node, string fallback) =>
        _renames.TryGetValue(node, out string? renamed) ? renamed : fallback;
}
