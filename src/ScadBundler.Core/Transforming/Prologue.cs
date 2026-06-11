using ScadBundler.Core.Ast;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Recovers the root's Customizer parameter prologue from an assembled bundle. The hardening passes run
/// after the inliner, which has already hoisted the prologue to the top and fenced the rest with a
/// synthesized <c>/* [Hidden] */</c>; this re-derives which leading statements are those parameters so
/// they can be kept verbatim (names never renamed — the end user reads them in OpenSCAD's Customizer).
/// </summary>
internal static class Prologue
{
    /// <summary>The set of top-level assignment nodes that form the Customizer parameter prologue (by
    /// reference identity). The leading run of literal top-level assignments, stopping at the synthesized
    /// <c>/* [Hidden] */</c> fence or the first non-literal/non-assignment statement.</summary>
    /// <param name="bundle">The assembled bundle.</param>
    /// <returns>The prologue assignment nodes.</returns>
    public static HashSet<AstNode> NodesOf(ScadFile bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var nodes = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        foreach (Statement statement in bundle.Statements)
        {
            if (HasHiddenFence(statement))
            {
                break; // the synthesized fence marks the first post-prologue (Hidden) statement
            }

            if (statement is AssignmentStatement assignment && IsCustomizerLiteral(assignment.Value))
            {
                nodes.Add(assignment);
                continue;
            }

            break;
        }

        return nodes;
    }

    /// <summary>Whether <paramref name="statement"/> carries the synthesized sticky <c>/* [Hidden] */</c>
    /// fence (distinguished from a user's own identical comment by <see cref="CommentTrivia.Sticky"/>).</summary>
    /// <param name="statement">The statement to test.</param>
    /// <returns><c>true</c> when the fence is present.</returns>
    public static bool HasHiddenFence(Statement statement) =>
        statement.LeadingTrivia.Any(t => t is CommentTrivia { Sticky: true, Text: "/* [Hidden] */" });

    // Mirrors OpenSCAD Expression::isLiteral() (the Customizer's parameter gate) — kept in sync with
    // Inliner.IsCustomizerLiteral: literals, unary over literals, all-literal vectors/ranges.
    private static bool IsCustomizerLiteral(Expression expression) => expression switch
    {
        NumberLiteral or StringLiteral or BooleanLiteral or UndefLiteral => true,
        UnaryExpression unary => IsCustomizerLiteral(unary.Operand),
        ParenthesizedExpression parenthesized => IsCustomizerLiteral(parenthesized.Inner),
        VectorExpression vector => vector.Elements.All(IsCustomizerLiteral),
        RangeExpression range => IsCustomizerLiteral(range.Start)
            && (range.Step is null || IsCustomizerLiteral(range.Step))
            && IsCustomizerLiteral(range.End),
        _ => false,
    };
}
