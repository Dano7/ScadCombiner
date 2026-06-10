using ScadBundler.IntegrationTests.TestSupport;
using Xunit;

namespace ScadBundler.IntegrationTests;

/// <summary>
/// Differential tests over real multi-file projects (GOEWS cleats, hex containers — include chains
/// up to three deep, Customizer parameter blocks, computed parameters). These are the fixtures that
/// caught the computed-params-hoist and last-wins-position regressions. <c>*-combined</c> and
/// <c>*.bundled</c> siblings in the directory are generated artifacts, not roots.
/// </summary>
public sealed class RealProjectTests
{
    /// <summary>Bundling a real project root must not change what OpenSCAD renders.</summary>
    [OpenScadTheory(IntegrationRequirements.RealProjects)]
    [InlineData("ForkedHolder.scad")] // include + Customizer literals + computed params
    [InlineData("HexContainer.scad")] // include + heavy parameter block
    [InlineData("CleatArray.scad")] // three-deep include chain (goews → utilities)
    [InlineData("grow-tent-fan-mount.scad")] // single file, annotated Customizer parameters
    [InlineData("goews.scad")] // library root with a mid-file include (defs only)
    public void RealProject_BundleRendersIdentically(string root) =>
        DifferentialAssert.BundleRendersIdentically(
            Path.Combine(IntegrationEnvironment.RealProjectsDirectory, root));
}
