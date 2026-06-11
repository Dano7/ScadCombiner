using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Renames every top-level user declaration (module, function, variable) and the references bound to it,
/// using the avalanche name generator (§5/§6.1). Tier-1-safe: names are arbitrary and references are
/// rewritten consistently from the post-inline semantic model. Never touches built-ins, <c>$</c>-special
/// variables (dynamic scope), the Customizer prologue (kept verbatim so the end user reads its real
/// names), or import/font path strings. Parameter and local-binding renaming is deferred (§6.1 note).
/// </summary>
internal sealed class IdentifierRenaming : IBundleTransform
{
    /// <inheritdoc/>
    public string Name => "identifier-renaming";

    /// <inheritdoc/>
    public bool NeedsModel => true;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context)
    {
        HashSet<AstNode> prologue = Prologue.NodesOf(bundle);

        // Renameable top-level declarations, in document order (the avalanche ordinal).
        var declarations = new List<Statement>();
        foreach (Statement statement in bundle.Statements)
        {
            switch (statement)
            {
                case ModuleDefinition or FunctionDefinition:
                    declarations.Add(statement);
                    break;
                case AssignmentStatement assignment
                    when !prologue.Contains(assignment) && !Builtins.IsSpecialVariable(assignment.Name):
                    declarations.Add(assignment);
                    break;
            }
        }

        if (declarations.Count == 0)
        {
            return bundle;
        }

        var generator = new NameGenerator(context.Profile, context.Seed, AstNodes.MentionedNames(bundle));
        string[] names = generator.AssignBatch(declarations.Count);

        var renames = new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < declarations.Count; i++)
        {
            Statement declaration = declarations[i];
            renames[declaration] = names[i];
            foreach (AstNode reference in context.Model.ReferencesTo(SymbolFor(declaration)))
            {
                renames[reference] = names[i];
            }

            context.RenamedCount++;
        }

        return new NameRewriter(renames).Rewrite(bundle);
    }

    // Any Symbol with the right Declaration node works — ReferencesTo keys on Declaration identity only.
    private static Symbol SymbolFor(Statement declaration) => declaration switch
    {
        ModuleDefinition module => new Symbol(SymbolKind.Module, module.Name, declaration.Span.File, declaration),
        FunctionDefinition function => new Symbol(SymbolKind.Function, function.Name, declaration.Span.File, declaration),
        AssignmentStatement assignment => new Symbol(SymbolKind.Variable, assignment.Name, declaration.Span.File, declaration),
        _ => throw new InvalidOperationException($"Not a declaration: {declaration.GetType().Name}"),
    };
}
