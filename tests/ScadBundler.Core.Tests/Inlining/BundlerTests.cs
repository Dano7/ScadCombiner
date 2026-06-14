using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// The public <see cref="Bundler"/> entry point end-to-end: the disk pipeline (loader → analyzer →
/// inliner), <c>OPENSCADPATH</c> appended to the search path, and never-throw on bad input.
/// </summary>
public sealed class BundlerTests
{
    [Fact]
    public void Bundle_DiskPipeline_InlinesInclude()
    {
        using var temp = new TempProject();
        temp.Write("main.scad", "include <lib.scad>\nbox();");
        temp.Write("lib.scad", "WALL = 2;\nmodule box() cube(WALL);");

        BundleResult result = Bundler.Bundle(temp.At("main.scad"), new BundleOptions([]));

        Assert.Contains(result.Bundled.Statements, s => s is ModuleDefinition { Name: "box" });
        Assert.Contains(result.Bundled.Statements, s => s is AssignmentStatement { Name: "WALL" });
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Bundle_AppendsOpenScadPath_ToSearchPath()
    {
        using var temp = new TempProject();
        temp.Write("proj/main.scad", "use <shared.scad>\nbox();");
        temp.Write("libs/shared.scad", "module box() cube(1);");

        string? previous = Environment.GetEnvironmentVariable("OPENSCADPATH");
        try
        {
            Environment.SetEnvironmentVariable("OPENSCADPATH", temp.At("libs"));
            BundleResult result = Bundler.Bundle(temp.At("proj/main.scad"), new BundleOptions([]));

            // `use`-imports are namespaced by construction (ADR 0001): box → shared__box.
            Assert.Contains(result.Bundled.Statements, s => s is ModuleDefinition { Name: "shared__box" });
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENSCADPATH", previous);
        }
    }

    [Fact]
    public void Bundle_WithinFileRedefinition_UnderLint_ReportsRedefinedOnce_NotPerStage()
    {
        using var temp = new TempProject();
        temp.Write("main.scad", "function f(x) = x + 1;\nfunction f(x) = x + 2;\necho(f(1));");

        // SB3004 is a static-lint finding, suppressed unless --lint (OpenSCAD silently last-wins).
        BundleResult result = Bundler.Bundle(temp.At("main.scad"), new BundleOptions([], Lint: true));

        // The semantic pass (within-file scope) and the inliner (merged-set collision) both detect the
        // same redefinition with an identical code/span/message; the bundler collapses the duplicate so
        // it surfaces once, not once per stage.
        Assert.Equal(1, result.Diagnostics.Count(d => d.Code == DiagnosticCode.DefinitionRedefined));
    }

    [Fact]
    public void Bundle_StaticLint_SuppressedByDefault_SurfacedUnderLint()
    {
        using var temp = new TempProject();
        // A module/function redefinition (SB3004) and an unknown reference (SB3005). OpenSCAD reports
        // neither at parse time — redefinitions silently last-win, and an unknown read is `undef`
        // (warned only at evaluation time). The bundle stays silent by default and surfaces both under
        // --lint; the collision is resolved (last-wins) either way.
        temp.Write("main.scad", "module m() cube(1);\nmodule m() sphere(unknown_var);\nm();");

        BundleResult quiet = Bundler.Bundle(temp.At("main.scad"), new BundleOptions([]));
        Assert.DoesNotContain(quiet.Diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
        Assert.DoesNotContain(quiet.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);

        BundleResult lint = Bundler.Bundle(temp.At("main.scad"), new BundleOptions([], Lint: true));
        Assert.Contains(lint.Diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
        Assert.Contains(lint.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void Bundle_MissingRoot_NeverThrows()
    {
        using var temp = new TempProject();

        BundleResult result = Bundler.Bundle(temp.At("does-not-exist.scad"), new BundleOptions([]));

        Assert.NotNull(result.Bundled);
        Assert.Empty(result.Bundled.Statements);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
    }

    private sealed class TempProject : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "scadbundler-tests", Guid.NewGuid().ToString("N"));

        public string At(string relative) =>
            Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relative, string content)
        {
            string full = At(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
