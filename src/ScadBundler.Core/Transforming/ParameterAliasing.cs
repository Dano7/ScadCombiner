using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// The headline Customizer requirement (§7): each root Customizer parameter keeps its original name
/// <b>only at the top</b> (so OpenSCAD's Customizer still lists it), then is consumed into a generated
/// alias used everywhere after. For each prologue parameter referenced in the body, a <i>computed</i>
/// assignment <c>&lt;alias&gt; = &lt;param&gt;;</c> is inserted right after the prologue and every body
/// reference is rewritten to the alias. Tier-1-safe: the alias is a reference-transparent rename of one
/// read; the prologue line is untouched (all three of OpenSCAD's parameter rules still select it); the
/// alias is computed so it is never itself a Customizer parameter. The generated alias name is a
/// temporary here — the later <see cref="IdentifierRenaming"/> pass gives it (and rewrites it to) the
/// final profile name.
/// </summary>
internal sealed class ParameterAliasing : IBundleTransform
{
    /// <inheritdoc/>
    public string Name => "parameter-aliasing";

    /// <inheritdoc/>
    public bool NeedsModel => true;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context)
    {
        HashSet<AstNode> prologue = Prologue.NodesOf(bundle);
        if (prologue.Count == 0)
        {
            return bundle;
        }

        var generator = new NameGenerator(context.Profile, context.Seed, AstNodes.MentionedNames(bundle));
        var renames = new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance);
        var aliases = new List<AssignmentStatement>();

        foreach (Statement statement in bundle.Statements)
        {
            if (statement is not AssignmentStatement parameter
                || !prologue.Contains(parameter)
                || Builtins.IsSpecialVariable(parameter.Name))
            {
                continue;
            }

            IReadOnlyList<AstNode> references = context.Model.ReferencesTo(
                new Symbol(SymbolKind.Variable, parameter.Name, parameter.Span.File, parameter));
            if (references.Count == 0)
            {
                continue; // parameter never read in the body — nothing to alias
            }

            string aliasName = generator.FreshName();
            foreach (AstNode reference in references)
            {
                renames[reference] = aliasName;
            }

            aliases.Add(new AssignmentStatement(aliasName, new Identifier(parameter.Name) { Span = parameter.Span })
            {
                Span = parameter.Span,
            });
            context.AliasedCount++;
        }

        if (aliases.Count == 0)
        {
            return bundle;
        }

        // Count the leading prologue statements on the original tree; the rename preserves order/count,
        // so the aliases insert at the same index in the rewritten tree (right after the prologue).
        int insertAt = 0;
        while (insertAt < bundle.Statements.Count && prologue.Contains(bundle.Statements[insertAt]))
        {
            insertAt++;
        }

        ScadFile rewritten = new NameRewriter(renames).Rewrite(bundle);
        var statements = new List<Statement>(rewritten.Statements);
        statements.InsertRange(insertAt, aliases);
        return rewritten with { Statements = statements };
    }
}
