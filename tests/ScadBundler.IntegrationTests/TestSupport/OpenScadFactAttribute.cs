using Xunit;

namespace ScadBundler.IntegrationTests.TestSupport;

/// <summary>External prerequisites a differential test needs beyond the OpenSCAD binary itself.</summary>
[Flags]
public enum IntegrationRequirements
{
    /// <summary>Only the OpenSCAD executable.</summary>
    None = 0,

    /// <summary>The official OpenSCAD source checkout (fixtures under <c>tests/data</c>, <c>examples/</c>).</summary>
    OpenScadCheckout = 1 << 0,

    /// <summary>The local directory of real-world multi-file projects.</summary>
    RealProjects = 1 << 1,
}

/// <summary>
/// A fact that runs only when the OpenSCAD binary (and any extra requirement) is present; in any
/// other environment the test is reported as skipped with the missing prerequisite as the reason.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpenScadFactAttribute : FactAttribute
{
    /// <summary>Creates the fact, probing the environment at discovery time.</summary>
    /// <param name="requires">Extra prerequisites beyond the OpenSCAD executable.</param>
    public OpenScadFactAttribute(IntegrationRequirements requires = IntegrationRequirements.None)
    {
        Requires = requires;
        Skip = IntegrationEnvironment.SkipReason(requires);
    }

    /// <summary>The declared prerequisites.</summary>
    public IntegrationRequirements Requires { get; }
}

/// <summary>The <see cref="TheoryAttribute"/> twin of <see cref="OpenScadFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpenScadTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the theory, probing the environment at discovery time.</summary>
    /// <param name="requires">Extra prerequisites beyond the OpenSCAD executable.</param>
    public OpenScadTheoryAttribute(IntegrationRequirements requires = IntegrationRequirements.None)
    {
        Requires = requires;
        Skip = IntegrationEnvironment.SkipReason(requires);
    }

    /// <summary>The declared prerequisites.</summary>
    public IntegrationRequirements Requires { get; }
}
