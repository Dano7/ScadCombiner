using System.Globalization;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Obfuscate dead-code injection (§6.6): appends plausible-looking, render-inert noise — a few uncalled
/// decoy module definitions and one disable-modifier (<c>*</c>) instantiation. Tier-1-safe: an uncalled
/// definition instantiates nothing (no CSG, no echo); the <c>*</c> modifier makes a subtree contribute
/// nothing to geometry (lexically equivalent to commenting it out). Only <c>*</c> (Disable) is used —
/// never <c>%</c> (Background, which can affect output) or <c>#</c> (Highlight). Names and shapes are
/// drawn from the avalanche seed.
/// </summary>
internal sealed class DeadCodeInjection : IBundleTransform
{
    /// <inheritdoc/>
    public string Name => "dead-code-injection";

    /// <inheritdoc/>
    public bool NeedsModel => false;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context)
    {
        var generator = new NameGenerator(context.Profile, context.Seed, AstNodes.MentionedNames(bundle));
        var injected = new List<Statement>();
        int decoyCount = 2 + (int)(NameGenerator.Mix(context.Seed) % 3); // 2..4 deterministic
        string? firstDecoy = null;

        for (int i = 0; i < decoyCount; i++)
        {
            ulong h = NameGenerator.Mix(context.Seed ^ (ulong)(0x100 + i));
            string moduleName = generator.FreshName();
            firstDecoy ??= moduleName;
            string parameter = generator.FreshName();

            Statement call = new ModuleInstantiation([], Primitive(h), [new Argument(null, Ref(parameter))], null)
            {
                Span = SourceSpan.Synthetic,
            };
            Statement module = new ModuleDefinition(
                moduleName,
                [new Parameter(parameter, Number((int)((h >> 8) % 9) + 1)) { Span = SourceSpan.Synthetic }],
                new BlockStatement([call]) { Span = SourceSpan.Synthetic })
            {
                Span = SourceSpan.Synthetic,
            };
            injected.Add(module);
            context.InjectedCount++;
        }

        // One render-inert disabled call (*) to a decoy — looks load-bearing, contributes no geometry.
        injected.Add(new ModuleInstantiation(
            [InstantiationModifier.Disable], firstDecoy!, [new Argument(null, Number(1))], null)
        {
            Span = SourceSpan.Synthetic,
        });
        context.InjectedCount++;

        var statements = new List<Statement>(bundle.Statements);
        statements.AddRange(injected);
        return bundle with { Statements = statements };
    }

    private static NumberLiteral Number(int value) =>
        new(value, value.ToString(CultureInfo.InvariantCulture)) { Span = SourceSpan.Synthetic };

    private static Identifier Ref(string identifierName) =>
        new(identifierName) { Span = SourceSpan.Synthetic };

    private static string Primitive(ulong h) => (h % 3) switch
    {
        0 => "sphere",
        1 => "cube",
        _ => "circle",
    };
}
