using ScadBundler.IntegrationTests.TestSupport;
using Xunit;

namespace ScadBundler.IntegrationTests;

/// <summary>
/// Differential tests over self-contained files from the official <c>examples/</c> tree: bundling a
/// single-file root is a near-identity transform, so these prove the parse → emit round trip is
/// semantically invisible to the official engine on real, expression-rich sources.
/// </summary>
public sealed class ExampleCorpusTests
{
    /// <summary>Bundling an official example must not change what OpenSCAD renders.</summary>
    [OpenScadTheory(IntegrationRequirements.OpenScadCheckout)]
    [InlineData(@"Old\example001.scad")]
    [InlineData(@"Basics\CSG.scad")]
    [InlineData(@"Basics\CSG-modules.scad")]
    public void Example_BundleRendersIdentically(string relativePath) =>
        DifferentialAssert.BundleRendersIdentically(Path.Combine(
            IntegrationEnvironment.OpenScadCheckout, "examples",
            relativePath.Replace('\\', Path.DirectorySeparatorChar)));
}
