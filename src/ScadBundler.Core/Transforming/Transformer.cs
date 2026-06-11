using System.Globalization;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// The hardening stage (Slice 7): runs a profile's ordered pass list over a flattened bundle, between
/// the inliner and the emitter. One engine, two mutually-exclusive profiles (<c>Minify</c>,
/// <c>Obfuscate</c>). Deterministic — every generated name derives from a single avalanche seed (a hash
/// of the post-inline bundle), so two runs are byte-identical yet a one-character source change reshuffles
/// every name. Never throws; emits an SB5009 summary when a profile ran.
/// </summary>
internal static class Transformer
{
    /// <summary>Applies <paramref name="profile"/> to <paramref name="bundle"/>. A no-op for
    /// <see cref="HardeningProfile.None"/> or an empty bundle (e.g. an <c>--on-collision error</c> failure).</summary>
    /// <param name="bundle">The flattened bundle from the inliner.</param>
    /// <param name="profile">The hardening profile to apply.</param>
    /// <param name="diagnostics">The diagnostic sink (SB5009/SB5010).</param>
    /// <returns>The transformed bundle.</returns>
    public static ScadFile Run(ScadFile bundle, HardeningProfile profile, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (profile == HardeningProfile.None || bundle.Statements.Count == 0)
        {
            return bundle;
        }

        // The avalanche seed: a hash of the canonical (default-emit) post-inline bundle. Any source change
        // that survives into the bundle perturbs this text and flips every generated name.
        ulong seed = NameGenerator.Fnv1a64(Emitter.Emit(bundle, EmitOptions.Default));
        var context = new TransformContext(profile, seed, diagnostics);

        foreach (IBundleTransform pass in Pipeline(profile))
        {
            if (pass.NeedsModel)
            {
                // Re-analyze the current bundle: prior passes rewrote node identities, so the model must
                // be rebuilt to bind references in the tree this pass actually receives.
                context.Model = SemanticAnalyzer.Analyze(bundle).Model;
            }

            bundle = pass.Apply(bundle, context);
        }

        diagnostics.Info(
            DiagnosticCode.Hardened,
            $"{ProfileName(profile)}: {Count(context.RenamedCount, "identifier")} renamed, "
            + $"{Count(context.RemovedCount, "definition")} tree-shaken, "
            + $"{Count(context.AliasedCount, "customizer parameter")} aliased, "
            + $"{Count(context.InjectedCount, "node")} injected.",
            new SourceSpan(bundle.Source, default, default));

        return bundle;
    }

    private static IReadOnlyList<IBundleTransform> Pipeline(HardeningProfile profile) => profile switch
    {
        // Aliasing before DCE (the alias keeps the prologue name reachable); DCE before renaming (don't
        // rename what you will delete); injection after renaming (injected nodes are generated opaque).
        HardeningProfile.Minify =>
        [
            new ParameterAliasing(),
            new DeadCodeElimination(),
            new LiteralCanonicalization(),
            new IdentifierRenaming(),
        ],
        HardeningProfile.Obfuscate =>
        [
            new ParameterAliasing(),
            new DeadCodeElimination(),
            new IdentifierRenaming(),
            new StringDecomposition(),
            new IndirectionInjection(),
            new DeadCodeInjection(),
        ],
        _ => [],
    };

    private static string ProfileName(HardeningProfile profile) =>
        profile == HardeningProfile.Minify ? "minify" : "obfuscate";

    private static string Count(int n, string noun) =>
        $"{n.ToString(CultureInfo.InvariantCulture)} {noun}{(n == 1 ? string.Empty : "s")}";
}
