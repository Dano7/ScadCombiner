using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Obfuscate indirection injection (§6.5): wraps a bounded, seed-selected subset of expressions in a
/// reference-transparent <c>let(&lt;b&gt; = e) &lt;b&gt;</c>. Tier-1-safe — a <c>let</c> binding evaluates
/// to the bound expression's value for <b>any</b> type (scalar, vector, string, range, function-value),
/// with no type assumptions (unlike <c>e+0</c>/<c>e*1</c>, which error on vectors — forbidden) and the
/// body always reads the just-bound value, so the same binding name can be reused without capture. Skips
/// the Customizer prologue (its values must stay literal for OpenSCAD's Customizer), path/font-sensitive
/// calls, and <c>echo</c>/<c>assert</c> subtrees. A fixed internal density bounds output growth.
/// </summary>
internal sealed class IndirectionInjection : IBundleTransform
{
    private const int MaxWraps = 256;

    private static readonly HashSet<string> PathSensitive = new(StringComparer.Ordinal)
    {
        "import", "surface", "text",
        "import_stl", "import_dxf", "import_off", "dxf_linear_extrude", "dxf_rotate_extrude",
    };

    /// <inheritdoc/>
    public string Name => "indirection-injection";

    /// <inheritdoc/>
    public bool NeedsModel => false;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context)
    {
        HashSet<AstNode> forbidden = CollectForbidden(bundle);
        string binding = new NameGenerator(context.Profile, context.Seed, AstNodes.MentionedNames(bundle)).FreshName();
        return new Injector(forbidden, binding, context).Rewrite(bundle);
    }

    // Subtrees never wrapped: Customizer parameter values (must stay literal) and path/font-sensitive calls.
    private static HashSet<AstNode> CollectForbidden(ScadFile bundle)
    {
        var forbidden = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        foreach (AstNode prologueNode in Prologue.NodesOf(bundle))
        {
            if (prologueNode is AssignmentStatement parameter)
            {
                foreach (AstNode node in AstNodes.DescendantsAndSelf(parameter.Value))
                {
                    forbidden.Add(node);
                }
            }
        }

        foreach (AstNode node in AstNodes.DescendantsAndSelf(bundle))
        {
            string? callName = node switch
            {
                ModuleInstantiation instantiation => instantiation.Name,
                FunctionCallExpression { Callee: Identifier identifier } => identifier.Name,
                _ => null,
            };

            if (callName is not null && PathSensitive.Contains(callName))
            {
                foreach (AstNode inner in AstNodes.DescendantsAndSelf(node))
                {
                    forbidden.Add(inner);
                }
            }
        }

        return forbidden;
    }

    private sealed class Injector : TreeRewriter
    {
        private readonly HashSet<AstNode> _forbidden;
        private readonly string _binding;
        private readonly TransformContext _context;
        private ulong _visited;
        private int _wraps;

        public Injector(HashSet<AstNode> forbidden, string binding, TransformContext context)
        {
            _forbidden = forbidden;
            _binding = binding;
            _context = context;
        }

        protected override Expression Transform(Expression rebuilt)
        {
            ulong roll = NameGenerator.Mix(_context.Seed ^ _visited++);
            if (_wraps >= MaxWraps
                || roll % 2 != 0
                || _forbidden.Contains(rebuilt)
                || !Eligible(rebuilt)
                || HasSideEffect(rebuilt))
            {
                return rebuilt;
            }

            _wraps++;
            _context.InjectedCount++;
            return new LetExpression(
                [new Binding(_binding, rebuilt) { Span = rebuilt.Span }],
                new Identifier(_binding) { Span = rebuilt.Span })
            {
                Span = rebuilt.Span,
            };
        }

        // Wrap whole values only — numbers, arithmetic, and call results. Deliberately NOT a bare
        // Identifier: an identifier in callee position (`f(x)`) names a function/module, and binding a
        // built-in or user `function` to a let variable is not a portable value in OpenSCAD.
        private static bool Eligible(Expression expression) =>
            expression is NumberLiteral or BinaryExpression or FunctionCallExpression;

        private static bool HasSideEffect(Expression expression) =>
            AstNodes.DescendantsAndSelf(expression).Any(node => node is EchoExpression or AssertExpression);
    }
}
