using System.Globalization;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Obfuscate string decomposition (§6.7): rewrites a string literal as <c>str(chr(c0), chr(c1), …)</c>
/// over its character codepoints. Tier-1-safe — <c>str</c>/<c>chr</c> over integer codepoints are
/// deterministic and platform-independent (no libm), so the re-decoded value is bit-identical. Restricted
/// to non-empty all-ASCII strings (every codepoint round-trips exactly through <c>chr</c>, no surrogate or
/// escaping hazard) and skipped entirely inside path/font-sensitive calls (<c>import</c>, <c>surface</c>,
/// <c>text</c>, the <c>dxf_*</c> family) where the literal string drives file/font resolution.
/// </summary>
internal sealed class StringDecomposition : IBundleTransform
{
    private static readonly HashSet<string> PathSensitive = new(StringComparer.Ordinal)
    {
        "import", "surface", "text",
        "import_stl", "import_dxf", "import_off", "dxf_linear_extrude", "dxf_rotate_extrude",
    };

    /// <inheritdoc/>
    public string Name => "string-decomposition";

    /// <inheritdoc/>
    public bool NeedsModel => false;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context)
    {
        HashSet<AstNode> protectedStrings = CollectProtected(bundle);
        return new Decomposer(protectedStrings, context).Rewrite(bundle);
    }

    // String literals never decomposed: those inside a path/font-sensitive call, and those that are (or
    // are part of) a Customizer parameter value — a decomposed string is not a literal, so OpenSCAD would
    // stop recognizing the parameter.
    private static HashSet<AstNode> CollectProtected(ScadFile bundle)
    {
        var protectedSet = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        foreach (AstNode prologueNode in Prologue.NodesOf(bundle))
        {
            if (prologueNode is AssignmentStatement parameter)
            {
                foreach (AstNode inner in AstNodes.DescendantsAndSelf(parameter.Value))
                {
                    if (inner is StringLiteral)
                    {
                        protectedSet.Add(inner);
                    }
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
                    if (inner is StringLiteral)
                    {
                        protectedSet.Add(inner);
                    }
                }
            }
        }

        return protectedSet;
    }

    private static bool IsDecomposable(StringLiteral literal) =>
        literal.Value.Length > 0 && literal.Value.All(c => c < 128);

    private sealed class Decomposer : TreeRewriter
    {
        private readonly HashSet<AstNode> _protected;
        private readonly TransformContext _context;

        public Decomposer(HashSet<AstNode> protectedStrings, TransformContext context)
        {
            _protected = protectedStrings;
            _context = context;
        }

        protected override Expression Transform(Expression rebuilt)
        {
            if (rebuilt is not StringLiteral literal
                || _protected.Contains(literal)
                || !IsDecomposable(literal))
            {
                return rebuilt;
            }

            _context.InjectedCount++;
            return Build(literal.Value, literal.Span);
        }

        private static FunctionCallExpression Build(string value, SourceSpan span)
        {
            var arguments = new List<Argument>(value.Length);
            foreach (char c in value)
            {
                Expression code = new NumberLiteral(c, ((int)c).ToString(CultureInfo.InvariantCulture)) { Span = span };
                Expression chr = new FunctionCallExpression(
                    new Identifier("chr") { Span = span },
                    [new Argument(null, code) { Span = span }])
                {
                    Span = span,
                };
                arguments.Add(new Argument(null, chr) { Span = span });
            }

            return new FunctionCallExpression(new Identifier("str") { Span = span }, arguments) { Span = span };
        }
    }
}
