using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Tree-shaking (§6.2): removes top-level module/function definitions never transitively referenced from
/// a root, and top-level assignments that are both unreferenced and side-effect-free. Tier-1-safe —
/// OpenSCAD has no dynamic dispatch of module/function names, so static reachability is sound;
/// unreachable definitions instantiate nothing (no CSG, no <c>echo</c>), and definitions do not execute
/// at definition time, so dropping an unreferenced one drops no side effect. Roots that seed reachability
/// (and are always kept): every executed top-level statement, the Customizer prologue, and any assignment
/// whose right-hand side contains an <c>echo</c>/<c>assert</c> (those fire at top-level evaluation, and
/// the harness compares <c>ECHO:</c> output).
/// </summary>
internal sealed class DeadCodeElimination : IBundleTransform
{
    /// <inheritdoc/>
    public string Name => "dead-code-elimination";

    /// <inheritdoc/>
    public bool NeedsModel => true;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context)
    {
        HashSet<AstNode> prologue = Prologue.NodesOf(bundle);

        var removable = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var roots = new List<Statement>();
        foreach (Statement statement in bundle.Statements)
        {
            bool isRemovable = statement switch
            {
                ModuleDefinition or FunctionDefinition => true,
                AssignmentStatement assignment => !prologue.Contains(assignment) && !HasSideEffect(assignment.Value),
                _ => false,
            };

            if (isRemovable)
            {
                removable.Add(statement);
            }
            else
            {
                roots.Add(statement); // executed statements, prologue, and side-effecting assignments
            }
        }

        if (removable.Count == 0)
        {
            return bundle;
        }

        // Mark-and-sweep: a removable declaration is live iff transitively referenced from a root.
        var live = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<AstNode>();

        void Mark(AstNode subtree)
        {
            foreach (AstNode node in AstNodes.DescendantsAndSelf(subtree))
            {
                if (context.Model.Resolve(node) is { Declaration: { } declaration }
                    && removable.Contains(declaration)
                    && live.Add(declaration))
                {
                    queue.Enqueue(declaration);
                }
            }
        }

        foreach (Statement root in roots)
        {
            Mark(root);
        }

        while (queue.Count > 0)
        {
            Mark(queue.Dequeue());
        }

        var kept = new List<Statement>(bundle.Statements.Count);
        int removed = 0;
        foreach (Statement statement in bundle.Statements)
        {
            if (removable.Contains(statement) && !live.Contains(statement))
            {
                removed++;
                continue;
            }

            kept.Add(statement);
        }

        if (removed == 0)
        {
            return bundle;
        }

        context.RemovedCount += removed;
        return bundle with { Statements = kept };
    }

    private static bool HasSideEffect(Expression value) =>
        AstNodes.DescendantsAndSelf(value).Any(node => node is EchoExpression or AssertExpression);
}
