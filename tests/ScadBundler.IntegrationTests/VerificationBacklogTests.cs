using ScadBundler.IntegrationTests.TestSupport;

namespace ScadBundler.IntegrationTests;

/// <summary>
/// The Development-Slices "Integration Verification Backlog" (V1–V3), proven by the differential
/// recipe against the official binary. V1 and V3 need a binary that still <em>evaluates</em> the
/// deprecated constructs — OpenSCAD 2021.01 (the default install) does, emitting <c>DEPRECATED:</c>
/// warnings the bundle is allowed to shed. If a future binary drops them, point <c>OPENSCAD_EXE</c>
/// at a 2021.01 install for these two facts.
/// </summary>
public sealed class VerificationBacklogTests
{
    private static string Fixture(string name) =>
        Path.Combine(IntegrationEnvironment.RepoRoot, "tests", "Corpus", "integration", name, "main.scad");

    /// <summary>V1 — <c>child()</c> ≡ <c>children(0)</c>, <c>child(n)</c> ≡ <c>children(n)</c> (gates SB5002).</summary>
    [OpenScadFact]
    public void V1_ChildToChildren_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(Fixture("V-001-child-children"));

    /// <summary>V2 — a <c>use</c>d definition sees its own file's constants; the consumer cannot override them.</summary>
    [OpenScadFact]
    public void V2_UseScoping_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(Fixture("V-002-use-scoping"));

    /// <summary>V3 — <c>assign(…)</c> ≡ <c>let(…)</c> for the binding-preserving rewrite (gates SB5001).</summary>
    [OpenScadFact]
    public void V3_AssignToLet_RendersIdentically() =>
        DifferentialAssert.BundleRendersIdentically(Fixture("V-003-assign-let"));
}
