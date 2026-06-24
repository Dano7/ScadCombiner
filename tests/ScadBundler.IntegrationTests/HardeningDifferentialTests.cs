using ScadBundler.Core.Inlining;
using ScadBundler.IntegrationTests.TestSupport;

namespace ScadBundler.IntegrationTests;

/// <summary>
/// The Slice-7 safety backstop: a hardened bundle (minify/obfuscate) must render <b>byte-identical
/// CSG</b>, emit identical <c>ECHO:</c>, and add no new warnings against the official OpenSCAD binary —
/// the empirical proof of the Tier-1 equivalence claim. The transform runs inside
/// <see cref="Bundler.Bundle(string, BundleOptions)"/> (via <see cref="BundleOptions.Hardening"/>); the
/// differential recipe renders the transformed bundle from an otherwise-empty temp dir (also proving
/// self-containment).
/// </summary>
public sealed class HardeningDifferentialTests
{
    private static string Fixture() =>
        Path.Combine(IntegrationEnvironment.RepoRoot, "tests", "Corpus", "integration", "T-001-harden", "main.scad");

    /// <summary>Minify: tree-shaking, identifier shortening, and parameter aliasing preserve geometry.</summary>
    [OpenScadFact]
    public void Minify_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(
            Fixture(), BundleOptions.Default with { Hardening = HardeningProfile.Minify });

    /// <summary>Obfuscate: opaque names, indirection, string decomposition, and decoys preserve geometry.</summary>
    [OpenScadFact]
    public void Obfuscate_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(
            Fixture(), BundleOptions.Default with { Hardening = HardeningProfile.Obfuscate });

    /// <summary>--parameters-first (ADR 0002): relocating the license header below the parameters is a
    /// comment-only change, so the geometry is unchanged.</summary>
    [OpenScadFact]
    public void ParametersFirst_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(
            Fixture(), BundleOptions.Default with { ParametersFirst = true });

    /// <summary>--parameters-first under minify: the relocated header rides on a tree-shakeable body
    /// statement, so this also proves DeadCodeElimination's sticky-trivia carry-forward is render-safe.</summary>
    [OpenScadFact]
    public void ParametersFirstMinify_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(
            Fixture(), BundleOptions.Default with { Hardening = HardeningProfile.Minify, ParametersFirst = true });
}
