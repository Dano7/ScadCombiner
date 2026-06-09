using System.Globalization;
using ScadBundler.Cli;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Text;
using Xunit;

// The OPENSCADPATH test mutates a process-wide environment variable; keep the CLI suite serial.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ScadBundler.Cli.Tests;

/// <summary>
/// End-to-end coverage of the <c>scadbundler</c> CLI (<see cref="BundleCommand.Run"/>): bundling to
/// stdout/file, each option, exit codes (0 clean / 1 error diagnostics / 2 usage), and the
/// <c>OPENSCADPATH</c> / <c>-p</c> search paths.
/// </summary>
public sealed class CliTests
{
    [Fact]
    public void Bundle_ToStdout_EmitsBundleAndExitsZero()
    {
        using var project = new TempProject(
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "WALL = 2;\nmodule box() cube(WALL);\ncube(99);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-"], out string stdout, out string stderr);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal("WALL = 2;\nmodule box() cube(WALL);\ncube(99);\nbox();\n", stdout);
    }

    [Fact]
    public void Bundle_ToDefaultPath_WritesFile()
    {
        using var project = new TempProject(("main.scad", "cube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad")], out _, out _);

        Assert.Equal(0, exit);
        string output = project.Path("main.bundled.scad");
        Assert.True(File.Exists(output));
        Assert.Equal("cube(1);\n", File.ReadAllText(output));
    }

    [Fact]
    public void Bundle_ToExplicitOutput_WritesThatFile()
    {
        using var project = new TempProject(("main.scad", "cube(1);"));
        string output = project.Path("out.scad");

        int exit = Run(project, ["bundle", project.Path("main.scad"), "--output", output], out _, out _);

        Assert.Equal(0, exit);
        Assert.Equal("cube(1);\n", File.ReadAllText(output));
    }

    [Fact]
    public void Minify_DropsWhitespace_AndStillParses()
    {
        using var project = new TempProject(("main.scad", "// header\nmodule a() cube(1);\na();"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--minify"], out string stdout, out _);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("\n", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("//", stdout, StringComparison.Ordinal);
        ParseResult reparsed = Parser.Parse(new SourceFile("min.scad", stdout));
        Assert.DoesNotContain(reparsed.Diagnostics, d => d.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void NoPreserveComments_DropsComments()
    {
        using var project = new TempProject(("main.scad", "// header\ncube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--no-preserve-comments"], out string stdout, out _);

        Assert.Equal(0, exit);
        Assert.Equal("cube(1);\n", stdout);
    }

    [Fact]
    public void OnCollisionPrefix_NamespacesIncludeDuplicates()
    {
        using var project = new TempProject(
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "--on-collision", "prefix"],
            out string stdout,
            out _);

        Assert.Equal(0, exit);
        Assert.Contains("a__part", stdout, StringComparison.Ordinal);
        Assert.Contains("b__part", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleLicenses_FlagIsAccepted()
    {
        using var project = new TempProject(("main.scad", "cube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--bundle-licenses"], out string stdout, out _);

        Assert.Equal(0, exit);
        Assert.Equal("cube(1);\n", stdout);
    }

    [Fact]
    public void DryRun_WritesNothing_ButReportsSummary()
    {
        using var project = new TempProject(("main.scad", "cube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "--dry-run"], out string stdout, out _);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(project.Path("main.bundled.scad")));
        Assert.Contains("dry-run", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_PrintsUnifiedDiff_AndWritesNothing()
    {
        using var project = new TempProject(
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "--diff"], out string stdout, out _);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(project.Path("main.bundled.scad")));
        Assert.Contains("---", stdout, StringComparison.Ordinal);
        Assert.Contains("+++", stdout, StringComparison.Ordinal);
        Assert.Contains("-include <lib.scad>", stdout, StringComparison.Ordinal); // the include line is removed
    }

    [Fact]
    public void Verbose_ListsInlinedFilesAndCounts()
    {
        using var project = new TempProject(
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--verbose"], out _, out string stderr);

        Assert.Equal(0, exit);
        Assert.Contains("1 file inlined (lib.scad)", stderr, StringComparison.Ordinal);
        Assert.Contains("0 renames", stderr, StringComparison.Ordinal);
        Assert.Contains("0 normalizations", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void CircularInclude_IsErrorDiagnostic_ExitsOne_NoOutput()
    {
        using var project = new TempProject(
            ("main.scad", "include <lib.scad>"),
            ("lib.scad", "include <main.scad>"));

        int exit = Run(project, ["bundle", project.Path("main.scad")], out _, out string stderr);

        Assert.Equal(1, exit);
        Assert.Contains("SB4002", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(project.Path("main.bundled.scad")));
    }

    [Fact]
    public void MissingInputFile_ExitsOne()
    {
        using var project = new TempProject();

        int exit = Run(project, ["bundle", project.Path("nope.scad")], out _, out string stderr);

        Assert.Equal(1, exit);
        Assert.Contains("cannot read input", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void BadArguments_ExitTwo()
    {
        Assert.Equal(2, RunArgs("bundle"));                            // no input
        Assert.Equal(2, RunArgs("bundle", "x.scad", "--bogus"));       // unknown option
        Assert.Equal(2, RunArgs("bundle", "x.scad", "--on-collision", "?")); // bad collision value
        Assert.Equal(2, RunArgs("render", "x.scad"));                  // unknown command
    }

    private static int RunArgs(params string[] args) =>
        BundleCommand.Run(args, new StringWriter(), new StringWriter());

    [Fact]
    public void NoArguments_PrintsUsageToStderr_ExitTwo()
    {
        var stderr = new StringWriter();
        int exit = BundleCommand.Run([], new StringWriter(), stderr);

        Assert.Equal(2, exit);
        Assert.Contains("Usage:", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_PrintsUsageToStdout_ExitZero()
    {
        var stdout = new StringWriter();
        int exit = BundleCommand.Run(["--help"], stdout, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPath_ResolvesUsedLibrary()
    {
        using var project = new TempProject(("main.scad", "use <shared.scad>\nwidget();"));
        using var lib = new TempProject(("shared.scad", "module widget() cube(2);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "-p", lib.Root],
            out string stdout,
            out _);

        Assert.Equal(0, exit);
        Assert.Contains("module widget() cube(2);", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenScadPath_IsHonored()
    {
        using var project = new TempProject(("main.scad", "use <shared.scad>\nwidget();"));
        using var lib = new TempProject(("shared.scad", "module widget() cube(3);"));

        string? previous = Environment.GetEnvironmentVariable("OPENSCADPATH");
        try
        {
            Environment.SetEnvironmentVariable("OPENSCADPATH", lib.Root);
            int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-"], out string stdout, out _);

            Assert.Equal(0, exit);
            Assert.Contains("module widget() cube(3);", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENSCADPATH", previous);
        }
    }

    private static int Run(TempProject project, string[] args, out string stdout, out string stderr)
    {
        _ = project; // fixtures live on disk; the CLI resolves them by the absolute paths in args
        var outWriter = new StringWriter(CultureInfo.InvariantCulture);
        var errWriter = new StringWriter(CultureInfo.InvariantCulture);
        int exit = BundleCommand.Run(args, outWriter, errWriter);
        stdout = Normalize(outWriter.ToString());
        stderr = Normalize(errWriter.ToString());
        return exit;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class TempProject : IDisposable
    {
        public TempProject(params (string Name, string Source)[] files)
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "scadbundler-cli-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Root);
            foreach ((string name, string source) in files)
            {
                File.WriteAllText(System.IO.Path.Combine(Root, name), source);
            }
        }

        public string Root { get; }

        public string Path(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }
}
