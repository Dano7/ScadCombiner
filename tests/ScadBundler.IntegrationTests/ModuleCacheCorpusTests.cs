using ScadBundler.IntegrationTests.TestSupport;
using Xunit;

namespace ScadBundler.IntegrationTests;

/// <summary>
/// Differential tests over the positive include/use fixtures of the official checkout's
/// <c>tests/data/modulecache-tests/</c>. Deliberately excluded: the error-path fixtures
/// (missing/circular includes — our loader makes cycles a hard SB4002 error by design where
/// OpenSCAD silently tolerates them) and <c>use-mcad.scad</c> (needs the external MCAD library).
/// </summary>
public sealed class ModuleCacheCorpusTests
{
    /// <summary>Bundling an official fixture root must not change what OpenSCAD renders.</summary>
    [OpenScadTheory(IntegrationRequirements.OpenScadCheckout)]
    [InlineData("simpleinclude.scad")] // include chain with a top-level instantiation in the leaf
    [InlineData("mainsubsub.scad")] // transitive use through a subdirectory
    [InlineData("modulewithinclude.scad")] // include + definition only (empty geometry both sides)
    [InlineData("includefrommodule.scad")] // use of a file that itself includes its constant
    [InlineData("main-use-include.scad")] // include executes top-level calls; use must not
    [InlineData("use.scad")] // used file's top-level call must not run
    [InlineData("moduleoverload.scad")] // local definition wins over the use'd one
    [InlineData("multiplemain.scad")] // diamond: two use'd libs sharing a common function
    public void ModuleCacheRoot_BundleRendersIdentically(string root) =>
        DifferentialAssert.BundleRendersIdentically(Path.Combine(
            IntegrationEnvironment.OpenScadCheckout, "tests", "data", "modulecache-tests", root));
}
