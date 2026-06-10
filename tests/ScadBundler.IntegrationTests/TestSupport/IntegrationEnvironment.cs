namespace ScadBundler.IntegrationTests.TestSupport;

/// <summary>
/// Probes the external prerequisites of the differential harness: the official OpenSCAD binary
/// (<c>OPENSCAD_EXE</c>, falling back to the default install location), the OpenSCAD source checkout
/// (<c>SCADBUNDLER_OPENSCAD_CHECKOUT</c>), and the real-project directory
/// (<c>SCADBUNDLER_REAL_PROJECTS</c>). Tests gate on <see cref="SkipReason"/> so an environment
/// without OpenSCAD skips them instead of failing.
/// </summary>
internal static class IntegrationEnvironment
{
    private static readonly Lazy<string> RepoRootValue = new(FindRepoRoot);
    private static readonly Lazy<string?> ExecutableValue = new(LocateExecutable);

    /// <summary>The repository root (the directory containing <c>ScadBundler.sln</c>).</summary>
    public static string RepoRoot => RepoRootValue.Value;

    /// <summary>The OpenSCAD executable, or <c>null</c> when none could be located.</summary>
    public static string? OpenScadExecutable => ExecutableValue.Value;

    /// <summary>The official OpenSCAD source checkout (ground truth; fixtures under <c>tests/data</c>).</summary>
    public static string OpenScadCheckout =>
        Environment.GetEnvironmentVariable("SCADBUNDLER_OPENSCAD_CHECKOUT") is { Length: > 0 } dir
            ? dir
            : @"C:\git\hub\openscad";

    /// <summary>The directory holding real-world multi-file OpenSCAD projects used as roots.</summary>
    public static string RealProjectsDirectory =>
        Environment.GetEnvironmentVariable("SCADBUNDLER_REAL_PROJECTS") is { Length: > 0 } dir
            ? dir
            : @"C:\git\dan\SCAD";

    /// <summary>
    /// The reason the calling test must be skipped, or <c>null</c> when every requirement is met.
    /// </summary>
    public static string? SkipReason(IntegrationRequirements requires)
    {
        if (Environment.GetEnvironmentVariable("SCADBUNDLER_SKIP_INTEGRATION") is { Length: > 0 })
        {
            return "Integration tests disabled by SCADBUNDLER_SKIP_INTEGRATION.";
        }

        if (OpenScadExecutable is null)
        {
            return "OpenSCAD executable not found — install OpenSCAD or point OPENSCAD_EXE at openscad(.com).";
        }

        if ((requires & IntegrationRequirements.OpenScadCheckout) != 0 && !Directory.Exists(OpenScadCheckout))
        {
            return $"OpenSCAD source checkout not found at '{OpenScadCheckout}' (override with SCADBUNDLER_OPENSCAD_CHECKOUT).";
        }

        if ((requires & IntegrationRequirements.RealProjects) != 0 && !Directory.Exists(RealProjectsDirectory))
        {
            return $"Real-project directory not found at '{RealProjectsDirectory}' (override with SCADBUNDLER_REAL_PROJECTS).";
        }

        return null;
    }

    private static string? LocateExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("OPENSCAD_EXE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // An explicit setting that points nowhere is "not found", not a silent fallback.
            return File.Exists(configured) ? configured : null;
        }

        // The .com console wrapper (not the GUI .exe) is required on Windows for stderr capture.
        string[] candidates =
        [
            @"C:\Program Files\OpenSCAD\openscad.com",
            "/usr/bin/openscad",
            "/usr/local/bin/openscad",
        ];
        return Array.Find(candidates, File.Exists);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ScadBundler.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate ScadBundler.sln above '{AppContext.BaseDirectory}'.");
    }
}
