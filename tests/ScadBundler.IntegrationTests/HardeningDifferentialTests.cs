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
}
