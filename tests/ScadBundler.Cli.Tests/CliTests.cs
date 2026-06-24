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
        // main.scad declares no Customizer parameters of its own, so the library's globals are fenced
        // out of the Customizer with a synthesized `/* [Hidden] */` boundary at the top; the default-on
        // attribution pass banners each inlined section with the statement that pulled it in.
        Assert.Equal(
            "/* [Hidden] */\n// ======== include <lib.scad> ========\n"
            + "WALL = 2;\nmodule box() cube(WALL);\ncube(99);\n"
            + "\n// ======== main.scad ========\nbox();\n",
            stdout);
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
        Assert.Contains("()cube(1);", stdout, StringComparison.Ordinal); // inner whitespace dropped (module renamed)
        Assert.Contains("// header", stdout, StringComparison.Ordinal);  // license header kept (sticky, Slice 7)
        ParseResult reparsed = Parser.Parse(new SourceFile("min.scad", stdout));
        Assert.DoesNotContain(reparsed.Diagnostics, d => d.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void NoPreserveComments_DropsComments()
    {
        using var project = new TempProject(("main.scad", "cube(1); // trailing note"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--no-preserve-comments"], out string stdout, out _);

        Assert.Equal(0, exit);
        Assert.Equal("cube(1);\n", stdout); // a non-header (trailing) comment is dropped — no sticky license involved
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
    public void OnCollisionError_Collision_ExitsOne_NoOutput()
    {
        using var project = new TempProject(
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "--on-collision", "error"],
            out string stdout,
            out string stderr);

        Assert.Equal(1, exit);
        Assert.Contains("SB5006", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
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
    public void BundleLicenses_DefaultOn_AggregatesHeadersAndReportsSb5007()
    {
        using var project = new TempProject(
            ("main.scad", "// (c) Root Author, CC-BY-4.0\ninclude <lib.scad>\nbox();"),
            ("lib.scad", "// (c) Lib Author, MIT\nmodule box() cube(1);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-"], out string stdout, out string stderr);

        Assert.Equal(0, exit);
        Assert.Contains("SB5007", stderr, StringComparison.Ordinal);
        Assert.Equal(
            "// (c) Root Author, CC-BY-4.0\n"
            + "// ======== file headers & licenses aggregated by ScadBundler ========\n"
            + "// -------- include <lib.scad> --------\n"
            + "// (c) Lib Author, MIT\n"
            + "// ====================================================================\n"
            + "// ======== include <lib.scad> ========\n"
            + "module box() cube(1);\n"
            + "\n// ======== main.scad ========\nbox();\n",
            stdout);
    }

    [Fact]
    public void NoBundleLicenses_ProducesUnannotatedBundle()
    {
        using var project = new TempProject(
            ("main.scad", "// (c) Root Author, CC-BY-4.0\ninclude <lib.scad>\nbox();"),
            ("lib.scad", "// (c) Lib Author, MIT\nmodule box() cube(1);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "--no-bundle-licenses"],
            out string stdout,
            out string stderr);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        // Pre-attribution behavior: the library header stays where it was and the root's header
        // (riding the flattened include line) is dropped — exactly what the default now prevents.
        Assert.Equal("// (c) Lib Author, MIT\nmodule box() cube(1);\nbox();\n", stdout);
    }

    [Fact]
    public void StaticLint_RedefinitionAndUnknown_SuppressedByDefault_SurfacedUnderLint()
    {
        // SB3004 (module redefinition) and SB3005 (unknown variable) are static approximations of
        // OpenSCAD's evaluation-time behavior — it silently last-wins on redefinition and reads an
        // unknown name as `undef`. The bundle is clean by default and reports them only under --lint.
        using var project = new TempProject(
            ("main.scad", "module m() cube(1);\nmodule m() sphere(missing_var);\nm();"));

        int quiet = Run(project, ["bundle", project.Path("main.scad"), "-o", "-"], out _, out string quietErr);
        Assert.Equal(0, quiet);
        Assert.DoesNotContain("SB3004", quietErr, StringComparison.Ordinal);
        Assert.DoesNotContain("SB3005", quietErr, StringComparison.Ordinal);

        int lint = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--lint"], out _, out string lintErr);
        Assert.Equal(0, lint);
        Assert.Contains("SB3004", lintErr, StringComparison.Ordinal);
        Assert.Contains("SB3005", lintErr, StringComparison.Ordinal);
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
    public void Obfuscate_RenamesIdentifiers_KeepsLicense_ReportsSb5009()
    {
        using var project = new TempProject(
            ("main.scad", "// (c) Author, MIT\nmodule widget(d) { cube(d); }\nwidget(5);"));

        int exit = Run(project, ["bundle", project.Path("main.scad"), "-o", "-", "--obfuscate"], out string stdout, out string stderr);

        Assert.Equal(0, exit);
        Assert.Contains("// (c) Author, MIT", stdout, StringComparison.Ordinal); // license survives
        Assert.DoesNotContain("widget", stdout, StringComparison.Ordinal);       // user name obfuscated away
        Assert.Contains("cube(", stdout, StringComparison.Ordinal);              // built-in preserved
        Assert.Contains("SB5009", stderr, StringComparison.Ordinal);
        ParseResult reparsed = Parser.Parse(new SourceFile("o.scad", stdout));
        Assert.DoesNotContain(reparsed.Diagnostics, d => d.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void StripLicense_DropsLicenseHeaderUnderMinify()
    {
        using var project = new TempProject(("main.scad", "// (c) Author, MIT\ncube(1);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "--minify", "--strip-license"],
            out string stdout,
            out _);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("//", stdout, StringComparison.Ordinal); // header dropped on request
        Assert.Contains("cube(1)", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void ParametersFirst_HoistsCustomizerParametersAboveTheLicenseHeader()
    {
        using var project = new TempProject(
            ("main.scad", "// (c) Root Author, CC-BY-4.0\ninclude <lib.scad>\n/* [Box] */\nwidth = 10;\npart(width);"),
            ("lib.scad", "// (c) Lib Author, MIT\nmodule part(w) cube(w);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "--parameters-first"],
            out string stdout,
            out _);

        Assert.Equal(0, exit);
        // The Customizer group + parameter lead the file…
        Assert.StartsWith("/* [Box] */\nwidth = 10;", stdout, StringComparison.Ordinal);
        // …and both license headers follow them (relocated, not dropped).
        int param = stdout.IndexOf("width = 10;", StringComparison.Ordinal);
        Assert.True(param < stdout.IndexOf("// (c) Root Author", StringComparison.Ordinal));
        Assert.Contains("// (c) Lib Author, MIT", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void MinifyAndObfuscate_AreMutuallyExclusive_ExitsTwo()
    {
        using var project = new TempProject(("main.scad", "cube(1);"));

        int exit = Run(
            project,
            ["bundle", project.Path("main.scad"), "-o", "-", "--minify", "--obfuscate"],
            out _,
            out string stderr);

        Assert.Equal(2, exit);
        Assert.Contains("mutually exclusive", stderr, StringComparison.Ordinal);
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
        Assert.Contains("module shared__widget() cube(2);", stdout, StringComparison.Ordinal); // use namespaced
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
            Assert.Contains("module shared__widget() cube(3);", stdout, StringComparison.Ordinal); // use namespaced
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
