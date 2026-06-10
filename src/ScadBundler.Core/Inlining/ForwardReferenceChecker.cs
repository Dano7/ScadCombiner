using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Semantics;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// Post-assembly safety net (SB5008): OpenSCAD evaluates top-level assignments in document order
/// (geometry is instantiated only afterwards, and module/function bodies resolve at call time), so a
/// top-level assignment that reads a variable whose first top-level assignment comes <i>later</i> in
/// the bundle evaluates to <c>undef</c>. The inliner must never introduce such an ordering by
/// reordering statements (e.g. hoisting); this pass walks the final bundle and reports any that slip
/// through — whether introduced by a transformation bug or present in the original sources.
/// </summary>
/// <remarks>
/// Only eagerly-evaluated expression positions are checked. Lazy bodies — function-literal bodies and
/// parameter defaults — are skipped: they evaluate at call time, when the file scope is complete.
/// Call callees are skipped too (a forward call to a function defined later is legal: definitions are
/// scope-wide). Special variables (<c>$…</c>) and built-in constants (<c>PI</c>) never warn.
/// </remarks>
internal static class ForwardReferenceChecker
{
    public static void Check(IReadOnlyList<Statement> statements, DiagnosticBag diagnostics)
    {
        // First top-level assignment position per name. Later reassignments don't matter here: a
        // read is safe iff some assignment of the name precedes it.
        var firstAssignment = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is AssignmentStatement assignment
                && !firstAssignment.ContainsKey(assignment.Name))
            {
                firstAssignment.Add(assignment.Name, i);
            }
        }

        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not AssignmentStatement assignment)
            {
                continue;
            }

            var reads = new List<Identifier>();
            CollectFreeReads(assignment.Value, [], reads);

            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (Identifier read in reads)
            {
                if (Builtins.IsSpecialVariable(read.Name) || Builtins.IsConstant(read.Name))
                {
                    continue;
                }

                if (firstAssignment.TryGetValue(read.Name, out int first)
                    && first > i
                    && reported.Add(read.Name))
                {
                    diagnostics.Warning(
                        DiagnosticCode.ForwardReference,
                        $"Top-level assignment '{assignment.Name}' reads '{read.Name}' before it is "
                        + "assigned in the bundle; OpenSCAD evaluates the read as undef.",
                        read.Span);
                }
            }
        }
    }

    // Collects identifier reads not bound by an enclosing let/for/comprehension binder. The bound
    // set is cloned at each binder so sibling subtrees don't see each other's names; bindings are
    // added sequentially, mirroring OpenSCAD's left-to-right let/for binding visibility.
    private static void CollectFreeReads(Expression expression, HashSet<string> bound, List<Identifier> reads)
    {
        switch (expression)
        {
            case NumberLiteral or StringLiteral or BooleanLiteral or UndefLiteral:
                break;

            case Identifier identifier:
                if (!bound.Contains(identifier.Name))
                {
                    reads.Add(identifier);
                }

                break;

            case VectorExpression vector:
                foreach (Expression element in vector.Elements)
                {
                    CollectFreeReads(element, bound, reads);
                }

                break;

            case RangeExpression range:
                CollectFreeReads(range.Start, bound, reads);
                if (range.Step is not null)
                {
                    CollectFreeReads(range.Step, bound, reads);
                }

                CollectFreeReads(range.End, bound, reads);
                break;

            case BinaryExpression binary:
                CollectFreeReads(binary.Left, bound, reads);
                CollectFreeReads(binary.Right, bound, reads);
                break;

            case UnaryExpression unary:
                CollectFreeReads(unary.Operand, bound, reads);
                break;

            case ConditionalExpression conditional:
                CollectFreeReads(conditional.Condition, bound, reads);
                CollectFreeReads(conditional.Then, bound, reads);
                CollectFreeReads(conditional.Else, bound, reads);
                break;

            case ParenthesizedExpression parenthesized:
                CollectFreeReads(parenthesized.Inner, bound, reads);
                break;

            case IndexExpression index:
                CollectFreeReads(index.Target, bound, reads);
                CollectFreeReads(index.Index, bound, reads);
                break;

            case MemberExpression member:
                CollectFreeReads(member.Target, bound, reads);
                break;

            case FunctionCallExpression call:
                // A callee identifier is a function reference, not a variable read: a forward call
                // to a function defined later in the bundle is legal (definitions are scope-wide).
                if (call.Callee is not Identifier)
                {
                    CollectFreeReads(call.Callee, bound, reads);
                }

                CollectFreeArgumentReads(call.Arguments, bound, reads);
                break;

            case LetExpression let:
                CollectBoundBody(let.Bindings, let.Body, bound, reads);
                break;

            case AssertExpression assert:
                CollectFreeArgumentReads(assert.Arguments, bound, reads);
                if (assert.Body is not null)
                {
                    CollectFreeReads(assert.Body, bound, reads);
                }

                break;

            case EchoExpression echo:
                CollectFreeArgumentReads(echo.Arguments, bound, reads);
                if (echo.Body is not null)
                {
                    CollectFreeReads(echo.Body, bound, reads);
                }

                break;

            case FunctionLiteral:
                break; // lazy: the body (and defaults) evaluate at call time, never at assignment time

            case ForComprehension comprehension:
                CollectBoundBody(comprehension.Bindings, comprehension.Body, bound, reads);
                break;

            case ForCComprehension comprehension:
            {
                var inner = new HashSet<string>(bound, StringComparer.Ordinal);
                foreach (Binding binding in comprehension.Init)
                {
                    CollectFreeReads(binding.Value, inner, reads);
                    inner.Add(binding.Name);
                }

                CollectFreeReads(comprehension.Condition, inner, reads);
                foreach (Binding binding in comprehension.Update)
                {
                    CollectFreeReads(binding.Value, inner, reads);
                }

                CollectFreeReads(comprehension.Body, inner, reads);
                break;
            }

            case IfComprehension comprehension:
                CollectFreeReads(comprehension.Condition, bound, reads);
                CollectFreeReads(comprehension.Then, bound, reads);
                if (comprehension.Else is not null)
                {
                    CollectFreeReads(comprehension.Else, bound, reads);
                }

                break;

            case LetComprehension comprehension:
                CollectBoundBody(comprehension.Bindings, comprehension.Body, bound, reads);
                break;

            case EachExpression each:
                CollectFreeReads(each.Value, bound, reads);
                break;
        }
    }

    private static void CollectBoundBody(
        IReadOnlyList<Binding> bindings, Expression body, HashSet<string> bound, List<Identifier> reads)
    {
        var inner = new HashSet<string>(bound, StringComparer.Ordinal);
        foreach (Binding binding in bindings)
        {
            CollectFreeReads(binding.Value, inner, reads);
            inner.Add(binding.Name);
        }

        CollectFreeReads(body, inner, reads);
    }

    private static void CollectFreeArgumentReads(
        IReadOnlyList<Argument> arguments, HashSet<string> bound, List<Identifier> reads)
    {
        foreach (Argument argument in arguments)
        {
            CollectFreeReads(argument.Value, bound, reads);
        }
    }
}
