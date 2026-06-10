using ScadBundler.IntegrationTests.TestSupport;
using Xunit;

namespace ScadBundler.IntegrationTests;

/// <summary>
/// Unit tests for the stderr classification used by the differential comparison. These need no
/// OpenSCAD binary and run in every environment.
/// </summary>
public sealed class OpenScadStderrTests
{
    [Fact]
    public void Normalize_StripsFileAndLineSuffix()
    {
        // Verbatim 2021.01 output shape (assign deprecation).
        string line =
            "DEPRECATED: The assign() module will be removed in future releases. Use a regular assignment instead. in file assigntest.scad, line 1";

        Assert.Equal(
            "DEPRECATED: The assign() module will be removed in future releases. Use a regular assignment instead.",
            OpenScadStderr.Normalize(line));
    }

    [Fact]
    public void Normalize_StripsCommaSeparatedSuffixWithAbsolutePath()
    {
        string line = "WARNING: Ignoring unknown variable 'x', in file C:/tmp/work dir/b.scad, line 12";

        Assert.Equal("WARNING: Ignoring unknown variable 'x'", OpenScadStderr.Normalize(line));
    }

    [Fact]
    public void Normalize_LeavesUnlocatedLinesUntouched()
    {
        // Verbatim 2021.01 output shape (child deprecation carries no location).
        string line = "DEPRECATED: child() will be removed in future releases. Use children() instead.";

        Assert.Equal(line, OpenScadStderr.Normalize(line));
    }

    [Theory]
    [InlineData("WARNING: undefined operation", true)]
    [InlineData("DEPRECATED: child() will be removed in future releases.", true)]
    [InlineData("ERROR: Parser error", true)]
    [InlineData("TRACE: called by 'f'", true)]
    [InlineData("ECHO: \"value\", 5", false)]
    [InlineData("Compiling design (CSG Tree generation)...", false)]
    public void IsWarningClass_RecognizesWarningPrefixes(string line, bool expected) =>
        Assert.Equal(expected, OpenScadStderr.IsWarningClass(line));

    [Fact]
    public void NewWarnings_AllowsWarningsToDisappear()
    {
        string[] original = ["DEPRECATED: assign", "WARNING: undef"];
        string[] bundled = ["WARNING: undef"];

        Assert.Empty(OpenScadStderr.NewWarnings(original, bundled));
    }

    [Fact]
    public void NewWarnings_FlagsIntroducedWarnings()
    {
        string[] original = ["WARNING: undef"];
        string[] bundled = ["WARNING: undef", "WARNING: unknown variable 'y'"];

        Assert.Equal(["WARNING: unknown variable 'y'"], OpenScadStderr.NewWarnings(original, bundled));
    }

    [Fact]
    public void NewWarnings_IsAMultiset_FlagsCountIncreases()
    {
        string[] original = ["WARNING: undef"];
        string[] bundled = ["WARNING: undef", "WARNING: undef"];

        Assert.Equal(["WARNING: undef"], OpenScadStderr.NewWarnings(original, bundled));
    }

    [Fact]
    public void EchoLines_FiltersVerbatimInOrder()
    {
        string[] stderr =
        [
            "Compiling design (CSG Tree generation)...",
            "ECHO: \"a\", 1",
            "WARNING: undef",
            "ECHO: \"b\", 2",
        ];

        Assert.Equal(["ECHO: \"a\", 1", "ECHO: \"b\", 2"], OpenScadStderr.EchoLines(stderr));
    }
}
