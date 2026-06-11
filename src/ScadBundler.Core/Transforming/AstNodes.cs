using ScadBundler.Core.Ast;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Read-only structural traversal helpers shared by the hardening transforms: enumerate a node's direct
/// children or its whole subtree, and collect the identifier names a subtree mentions. Mirrors the
/// <see cref="IAstVisitor{TResult}"/> child structure but as a plain enumerator, which is more
/// convenient for the analysis passes (reachability, reserved-name collection) than a full visitor.
/// </summary>
internal static class AstNodes
{
    /// <summary>The node itself followed by every descendant node, depth-first (pre-order).</summary>
    /// <param name="root">The subtree root.</param>
    /// <returns>All nodes in the subtree, root first.</returns>
    public static IEnumerable<AstNode> DescendantsAndSelf(AstNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var stack = new Stack<AstNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            AstNode node = stack.Pop();
            yield return node;

            // Push children in reverse so the first child is visited first (document order).
            List<AstNode> children = Children(node);
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    /// <summary>Every identifier <i>name</i> that appears anywhere in <paramref name="root"/> — declaration
    /// names, references, parameter/binding names — so a name generator can avoid all of them.</summary>
    /// <param name="root">The subtree to scan.</param>
    /// <returns>The set of names mentioned.</returns>
    public static HashSet<string> MentionedNames(AstNode root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (AstNode node in DescendantsAndSelf(root))
        {
            switch (node)
            {
                case Identifier identifier: names.Add(identifier.Name); break;
                case ModuleInstantiation instantiation: names.Add(instantiation.Name); break;
                case ModuleDefinition module: names.Add(module.Name); break;
                case FunctionDefinition function: names.Add(function.Name); break;
                case AssignmentStatement assignment: names.Add(assignment.Name); break;
                case Parameter parameter: names.Add(parameter.Name); break;
                case Binding binding: names.Add(binding.Name); break;
                case Argument { Name: { } argName }: names.Add(argName); break;
                case MemberExpression member: names.Add(member.Member); break;
            }
        }

        return names;
    }

    private static List<AstNode> Children(AstNode node)
    {
        var children = new List<AstNode>();
        switch (node)
        {
            case ScadFile file:
                children.AddRange(file.Statements);
                break;
            case ModuleDefinition module:
                children.AddRange(module.Parameters);
                children.Add(module.Body);
                break;
            case FunctionDefinition function:
                children.AddRange(function.Parameters);
                children.Add(function.Body);
                break;
            case AssignmentStatement assignment:
                children.Add(assignment.Value);
                break;
            case ModuleInstantiation instantiation:
                children.AddRange(instantiation.Arguments);
                if (instantiation.Child is not null)
                {
                    children.Add(instantiation.Child);
                }

                break;
            case BlockStatement block:
                children.AddRange(block.Statements);
                break;
            case IfStatement branch:
                children.Add(branch.Condition);
                children.Add(branch.Then);
                if (branch.Else is not null)
                {
                    children.Add(branch.Else);
                }

                break;
            case ForStatement loop:
                children.AddRange(loop.Bindings);
                children.Add(loop.Body);
                break;
            case IntersectionForStatement loop:
                children.AddRange(loop.Bindings);
                children.Add(loop.Body);
                break;
            case LetStatement let:
                children.AddRange(let.Bindings);
                children.Add(let.Body);
                break;
            case VectorExpression vector:
                children.AddRange(vector.Elements);
                break;
            case RangeExpression range:
                children.Add(range.Start);
                if (range.Step is not null)
                {
                    children.Add(range.Step);
                }

                children.Add(range.End);
                break;
            case BinaryExpression binary:
                children.Add(binary.Left);
                children.Add(binary.Right);
                break;
            case UnaryExpression unary:
                children.Add(unary.Operand);
                break;
            case ConditionalExpression conditional:
                children.Add(conditional.Condition);
                children.Add(conditional.Then);
                children.Add(conditional.Else);
                break;
            case ParenthesizedExpression parenthesized:
                children.Add(parenthesized.Inner);
                break;
            case IndexExpression index:
                children.Add(index.Target);
                children.Add(index.Index);
                break;
            case MemberExpression member:
                children.Add(member.Target);
                break;
            case FunctionCallExpression call:
                children.Add(call.Callee);
                children.AddRange(call.Arguments);
                break;
            case LetExpression let:
                children.AddRange(let.Bindings);
                children.Add(let.Body);
                break;
            case AssertExpression assert:
                children.AddRange(assert.Arguments);
                if (assert.Body is not null)
                {
                    children.Add(assert.Body);
                }

                break;
            case EchoExpression echo:
                children.AddRange(echo.Arguments);
                if (echo.Body is not null)
                {
                    children.Add(echo.Body);
                }

                break;
            case FunctionLiteral literal:
                children.AddRange(literal.Parameters);
                children.Add(literal.Body);
                break;
            case ForComprehension comprehension:
                children.AddRange(comprehension.Bindings);
                children.Add(comprehension.Body);
                break;
            case ForCComprehension comprehension:
                children.AddRange(comprehension.Init);
                children.Add(comprehension.Condition);
                children.AddRange(comprehension.Update);
                children.Add(comprehension.Body);
                break;
            case IfComprehension comprehension:
                children.Add(comprehension.Condition);
                children.Add(comprehension.Then);
                if (comprehension.Else is not null)
                {
                    children.Add(comprehension.Else);
                }

                break;
            case LetComprehension comprehension:
                children.AddRange(comprehension.Bindings);
                children.Add(comprehension.Body);
                break;
            case EachExpression each:
                children.Add(each.Value);
                break;
            case Parameter { DefaultValue: { } defaultValue }:
                children.Add(defaultValue);
                break;
            case Argument argument:
                children.Add(argument.Value);
                break;
            case Binding binding:
                children.Add(binding.Value);
                break;
            default:
                // Leaves: literals, Identifier, Include/Use/Empty statements, parameter without default.
                break;
        }

        return children;
    }
}
