using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Semantics;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Shared mutable state for one hardening run: the active profile and avalanche seed (§5), the
/// diagnostic sink, the semantic model of the <i>current</i> bundle (refreshed by the
/// <see cref="Transformer"/> before each model-needing pass), and running counters for the SB5009
/// summary.
/// </summary>
internal sealed class TransformContext
{
    /// <summary>Creates a context for a run.</summary>
    /// <param name="profile">The active hardening profile.</param>
    /// <param name="seed">The global avalanche seed.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    public TransformContext(HardeningProfile profile, ulong seed, DiagnosticBag diagnostics)
    {
        Profile = profile;
        Seed = seed;
        Diagnostics = diagnostics;
    }

    /// <summary>The active hardening profile.</summary>
    public HardeningProfile Profile { get; }

    /// <summary>The global avalanche seed (a hash of the post-inline bundle).</summary>
    public ulong Seed { get; }

    /// <summary>The diagnostic sink.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>The semantic model of the current bundle; set by the <see cref="Transformer"/> before each
    /// pass whose <see cref="IBundleTransform.NeedsModel"/> is <c>true</c>.</summary>
    public ISemanticModel Model { get; set; } = EmptyModel.Instance;

    /// <summary>Count of declarations renamed (SB5009).</summary>
    public int RenamedCount { get; set; }

    /// <summary>Count of definitions tree-shaken (SB5009).</summary>
    public int RemovedCount { get; set; }

    /// <summary>Count of Customizer parameters aliased (SB5009).</summary>
    public int AliasedCount { get; set; }

    /// <summary>Count of decoy/indirection nodes injected (SB5009).</summary>
    public int InjectedCount { get; set; }
}
