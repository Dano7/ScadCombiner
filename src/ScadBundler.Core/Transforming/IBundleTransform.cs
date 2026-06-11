using ScadBundler.Core.Ast;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// One AST→AST hardening pass over a flattened bundle (Slice 7). Each pass is independently
/// Tier-1-safe (CSG-tree-preserving) and rebuilds changed subtrees immutably. The <see cref="Transformer"/>
/// composes passes into the <c>Minify</c>/<c>Obfuscate</c> profiles.
/// </summary>
internal interface IBundleTransform
{
    /// <summary>A stable identifier for diagnostics/logging.</summary>
    string Name { get; }

    /// <summary>Whether this pass reads <see cref="TransformContext.Model"/>; if so the
    /// <see cref="Transformer"/> re-analyzes the current bundle and refreshes the model before
    /// invoking <see cref="Apply"/> (node identities change between passes).</summary>
    bool NeedsModel { get; }

    /// <summary>Applies the pass, returning the transformed bundle (or the same instance if unchanged).</summary>
    /// <param name="bundle">The current bundle AST.</param>
    /// <param name="context">Shared per-run state (profile, seed, model, diagnostics, counters).</param>
    /// <returns>The transformed bundle.</returns>
    ScadFile Apply(ScadFile bundle, TransformContext context);
}
