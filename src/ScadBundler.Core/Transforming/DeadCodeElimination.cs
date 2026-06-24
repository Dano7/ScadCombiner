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
/// (and are always kept): every executed top-level statement, the Customizer prologue, any assignment
/// whose right-hand side contains an <c>echo</c>/<c>assert</c> (those fire at top-level evaluation, and
/// the harness compares <c>ECHO:</c> output), and every <b><c>$</c>-special-variable assignment</b>.
/// A top-level <c>$foo = …</c> establishes a <i>dynamically-scoped</i> default that any module reached at
/// render time may read; those reads resolve to no symbol in the static model (special variables are not
/// lexically bound), so the mark-and-sweep can never see the edge and would wrongly drop the default —
/// breaking, e.g., BOSL2's <c>$tags_shown = "ALL"</c> / <c>$transform = IDENT</c> attachment globals.
/// Special-variable assignments are therefore always kept (we cannot prove a dynamic read absent).
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
                // A `$`-special-variable default is read through dynamic scope, which the static reference
                // model can't see — never tree-shake it (it would drop a default a render-time read needs).
                AssignmentStatement assignment =>
                    !prologue.Contains(assignment)
                    && !Builtins.IsSpecialVariable(assignment.Name)
                    && !HasSideEffect(assignment.Value),
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

        // Sticky leading trivia rescued from dropped statements (the aggregated license header and the
        // synthesized /* [Hidden] */ fence, both Sticky). Dropping their host statement must not drop
        // them — carry them forward onto the next surviving statement so the license and the Customizer
        // fence still lead the body (the --parameters-first relocation rides on a body statement, which
        // is exactly the kind of node tree-shaking can remove).
        var rescued = new List<Trivia>();
        foreach (Statement statement in bundle.Statements)
        {
            if (removable.Contains(statement) && !live.Contains(statement))
            {
                removed++;
                rescued.AddRange(statement.LeadingTrivia.Where(static t => t is CommentTrivia { Sticky: true }));
                continue;
            }

            if (rescued.Count > 0)
            {
                kept.Add(statement with { LeadingTrivia = [.. rescued, .. statement.LeadingTrivia] });
                rescued.Clear();
                continue;
            }

            kept.Add(statement);
        }

        if (removed == 0)
        {
            return bundle;
        }

        // If every statement after the rescued trivia's host was also removed (e.g. a parameters-only
        // bundle whose entire body tree-shakes away), no later statement caught it. Re-home the license
        // atop the surviving statements so attribution is never silently dropped. The /* [Hidden] */
        // fence is deliberately NOT re-homed: with no body global left it has nothing to hide, and
        // placing it above the parameters would wrongly hide them from the Customizer.
        if (rescued.Count > 0 && kept.Count > 0)
        {
            List<Trivia> license =
                [.. rescued.Where(static t => t is not CommentTrivia { Sticky: true, Text: "/* [Hidden] */" })];
            if (license.Count > 0)
            {
                kept[0] = kept[0] with { LeadingTrivia = [.. license, .. kept[0].LeadingTrivia] };
            }
        }

        context.RemovedCount += removed;
        return bundle with { Statements = kept };
    }

    private static bool HasSideEffect(Expression value) =>
        AstNodes.DescendantsAndSelf(value).Any(node => node is EchoExpression or AssertExpression);
}
