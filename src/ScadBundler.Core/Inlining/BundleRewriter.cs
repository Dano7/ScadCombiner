using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Semantics;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// The single rewrite pass that finalizes the bundle tree: it renames declarations and the references
/// bound to them (collision resolution, keyed by original-node identity per AST-Reference §15.6) and
/// normalizes deprecated constructs (<c>assign</c>→<c>let</c> SB5001, <c>child</c>→<c>children</c>
/// SB5002, deprecated built-ins preserved with SB5003). Every produced node is fresh (immutable
/// <c>with</c>-rebuild) and reuses its origin node's span/trivia.
/// </summary>
internal sealed class BundleRewriter
{
    private readonly IReadOnlyDictionary<AstNode, string> _renames;
    private readonly DiagnosticBag _diagnostics;

    public BundleRewriter(IReadOnlyDictionary<AstNode, string> renames, DiagnosticBag diagnostics)
    {
        _renames = renames;
        _diagnostics = diagnostics;
    }

    public Statement RewriteStatement(Statement statement)
    {
        switch (statement)
        {
            case ModuleDefinition module:
                return module with
                {
                    Name = Renamed(module, module.Name),
                    Parameters = RewriteParameters(module.Parameters),
                    Body = RewriteStatement(module.Body),
                };

            case FunctionDefinition function:
                return function with
                {
                    Name = Renamed(function, function.Name),
                    Parameters = RewriteParameters(function.Parameters),
                    Body = RewriteExpression(function.Body),
                };

            case AssignmentStatement assignment:
                return assignment with
                {
                    Name = Renamed(assignment, assignment.Name),
                    Value = RewriteExpression(assignment.Value),
                };

            case ModuleInstantiation instantiation:
                return RewriteInstantiation(instantiation);

            case BlockStatement block:
                return block with { Statements = RewriteStatements(block.Statements) };

            case IfStatement branch:
                return branch with
                {
                    Condition = RewriteExpression(branch.Condition),
                    Then = RewriteStatement(branch.Then),
                    Else = branch.Else is null ? null : RewriteStatement(branch.Else),
                };

            case ForStatement loop:
                return loop with
                {
                    Bindings = RewriteBindings(loop.Bindings),
                    Body = RewriteStatement(loop.Body),
                };

            case IntersectionForStatement loop:
                return loop with
                {
                    Bindings = RewriteBindings(loop.Bindings),
                    Body = RewriteStatement(loop.Body),
                };

            case LetStatement let:
                return let with
                {
                    Bindings = RewriteBindings(let.Bindings),
                    Body = RewriteStatement(let.Body),
                };

            default:
                // IncludeStatement / UseStatement (font pass-through) / EmptyStatement: no inner refs.
                return statement;
        }
    }

    public Expression RewriteExpression(Expression expression)
    {
        switch (expression)
        {
            case Identifier identifier:
                return identifier with { Name = Renamed(identifier, identifier.Name) };

            case NumberLiteral or StringLiteral or BooleanLiteral or UndefLiteral:
                return expression;

            case VectorExpression vector:
                return vector with { Elements = RewriteExpressions(vector.Elements) };

            case RangeExpression range:
                return range with
                {
                    Start = RewriteExpression(range.Start),
                    Step = range.Step is null ? null : RewriteExpression(range.Step),
                    End = RewriteExpression(range.End),
                };

            case BinaryExpression binary:
                return binary with
                {
                    Left = RewriteExpression(binary.Left),
                    Right = RewriteExpression(binary.Right),
                };

            case UnaryExpression unary:
                return unary with { Operand = RewriteExpression(unary.Operand) };

            case ConditionalExpression conditional:
                return conditional with
                {
                    Condition = RewriteExpression(conditional.Condition),
                    Then = RewriteExpression(conditional.Then),
                    Else = RewriteExpression(conditional.Else),
                };

            case ParenthesizedExpression parenthesized:
                return parenthesized with { Inner = RewriteExpression(parenthesized.Inner) };

            case IndexExpression index:
                return index with
                {
                    Target = RewriteExpression(index.Target),
                    Index = RewriteExpression(index.Index),
                };

            case MemberExpression member:
                return member with { Target = RewriteExpression(member.Target) };

            case FunctionCallExpression call:
                return call with
                {
                    Callee = RewriteExpression(call.Callee),
                    Arguments = RewriteArguments(call.Arguments),
                };

            case LetExpression let:
                return let with
                {
                    Bindings = RewriteBindings(let.Bindings),
                    Body = RewriteExpression(let.Body),
                };

            case AssertExpression assert:
                return assert with
                {
                    Arguments = RewriteArguments(assert.Arguments),
                    Body = assert.Body is null ? null : RewriteExpression(assert.Body),
                };

            case EchoExpression echo:
                return echo with
                {
                    Arguments = RewriteArguments(echo.Arguments),
                    Body = echo.Body is null ? null : RewriteExpression(echo.Body),
                };

            case FunctionLiteral literal:
                return literal with
                {
                    Parameters = RewriteParameters(literal.Parameters),
                    Body = RewriteExpression(literal.Body),
                };

            case ForComprehension comprehension:
                return comprehension with
                {
                    Bindings = RewriteBindings(comprehension.Bindings),
                    Body = RewriteExpression(comprehension.Body),
                };

            case ForCComprehension comprehension:
                return comprehension with
                {
                    Init = RewriteBindings(comprehension.Init),
                    Condition = RewriteExpression(comprehension.Condition),
                    Update = RewriteBindings(comprehension.Update),
                    Body = RewriteExpression(comprehension.Body),
                };

            case IfComprehension comprehension:
                return comprehension with
                {
                    Condition = RewriteExpression(comprehension.Condition),
                    Then = RewriteExpression(comprehension.Then),
                    Else = comprehension.Else is null ? null : RewriteExpression(comprehension.Else),
                };

            case LetComprehension comprehension:
                return comprehension with
                {
                    Bindings = RewriteBindings(comprehension.Bindings),
                    Body = RewriteExpression(comprehension.Body),
                };

            case EachExpression each:
                return each with { Value = RewriteExpression(each.Value) };

            default:
                return expression;
        }
    }

    private Statement RewriteInstantiation(ModuleInstantiation instantiation)
    {
        if (TryNormalizeAssign(instantiation, out Statement? rewritten))
        {
            return rewritten;
        }

        if (instantiation.Name == "child")
        {
            return NormalizeChild(instantiation);
        }

        if (Builtins.IsDeprecatedPreserved(instantiation.Name))
        {
            _diagnostics.Info(
                DiagnosticCode.DeprecatedBuiltinPreserved,
                $"'{instantiation.Name}' is deprecated in OpenSCAD; preserved unchanged. Consider migrating to its modern equivalent.",
                instantiation.Span);
        }

        return instantiation with
        {
            Name = Renamed(instantiation, instantiation.Name),
            Arguments = RewriteArguments(instantiation.Arguments),
            Child = instantiation.Child is null ? null : RewriteStatement(instantiation.Child),
        };
    }

    // `assign(a = …, b = …) child` → `let(a = …, b = …) child` (SB5001), bindings preserved verbatim.
    // Only rewritten when it has a child and every argument is named (otherwise it is not a valid
    // `let`); left untouched and unflagged otherwise.
    private bool TryNormalizeAssign(ModuleInstantiation instantiation, out Statement rewritten)
    {
        if (instantiation.Name != "assign"
            || instantiation.Child is null
            || instantiation.Arguments.Any(argument => argument.Name is null))
        {
            rewritten = instantiation;
            return false;
        }

        var bindings = new List<Binding>(instantiation.Arguments.Count);
        foreach (Argument argument in instantiation.Arguments)
        {
            bindings.Add(new Binding(argument.Name!, RewriteExpression(argument.Value)) { Span = argument.Span });
        }

        _diagnostics.Warning(
            DiagnosticCode.AssignNormalized,
            "'assign' is deprecated; rewritten to 'let'. (Behavior preserved.)",
            instantiation.Span);

        rewritten = new LetStatement(bindings, RewriteStatement(instantiation.Child))
        {
            Span = instantiation.Span,
            LeadingTrivia = instantiation.LeadingTrivia,
            TrailingTrivia = instantiation.TrailingTrivia,
            BlankLineBefore = instantiation.BlankLineBefore,
        };
        return true;
    }

    // `child()` → `children(0)` (first child); `child(n)` → `children(n)` (SB5002).
    private ModuleInstantiation NormalizeChild(ModuleInstantiation instantiation)
    {
        _diagnostics.Warning(
            DiagnosticCode.ChildNormalized,
            "'child(...)' is deprecated; rewritten to 'children(...)'.",
            instantiation.Span);

        IReadOnlyList<Argument> arguments = instantiation.Arguments.Count == 0
            ? [new Argument(null, new NumberLiteral(0, "0") { Span = instantiation.Span }) { Span = instantiation.Span }]
            : RewriteArguments(instantiation.Arguments);

        return instantiation with
        {
            Name = "children",
            Arguments = arguments,
            Child = instantiation.Child is null ? null : RewriteStatement(instantiation.Child),
        };
    }

    private List<Statement> RewriteStatements(IReadOnlyList<Statement> statements)
    {
        var result = new List<Statement>(statements.Count);
        foreach (Statement statement in statements)
        {
            result.Add(RewriteStatement(statement));
        }

        return result;
    }

    private List<Expression> RewriteExpressions(IReadOnlyList<Expression> expressions)
    {
        var result = new List<Expression>(expressions.Count);
        foreach (Expression expression in expressions)
        {
            result.Add(RewriteExpression(expression));
        }

        return result;
    }

    private List<Argument> RewriteArguments(IReadOnlyList<Argument> arguments)
    {
        var result = new List<Argument>(arguments.Count);
        foreach (Argument argument in arguments)
        {
            result.Add(argument with { Value = RewriteExpression(argument.Value) });
        }

        return result;
    }

    private List<Parameter> RewriteParameters(IReadOnlyList<Parameter> parameters)
    {
        var result = new List<Parameter>(parameters.Count);
        foreach (Parameter parameter in parameters)
        {
            result.Add(parameter.DefaultValue is null
                ? parameter
                : parameter with { DefaultValue = RewriteExpression(parameter.DefaultValue) });
        }

        return result;
    }

    private List<Binding> RewriteBindings(IReadOnlyList<Binding> bindings)
    {
        var result = new List<Binding>(bindings.Count);
        foreach (Binding binding in bindings)
        {
            result.Add(binding with { Value = RewriteExpression(binding.Value) });
        }

        return result;
    }

    private string Renamed(AstNode node, string fallback) =>
        _renames.TryGetValue(node, out string? renamed) ? renamed : fallback;
}
