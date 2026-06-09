using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using ScadBundler.Core.Text;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// Edge and defensive paths of the loader and inliner: graceful degradation on read failures and bad
/// paths (the never-throw guarantee), transitive/deduped use imports, function imports, cross-include
/// variable reassignment, and the namespacing scheme's stem sanitization and secondary-clash suffixes.
/// </summary>
public sealed class Slice5EdgeCoverageTests
{
    private static BundleOptions Options(params string[] libraryPaths) => new(libraryPaths);

    [Fact]
    public void Use_NotFound_ReportsSB4001()
    {
        var fs = new InMemoryFileSystem().Add("/proj/main.scad", "use <missing.scad>\ncube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Null(Assert.Single(graph.Root.Uses).Target);
    }

    [Fact]
    public void LibraryPath_SecondEntry_ConsultedWhenFirstMisses()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "use <shared.scad>")
            .Add("/libs/shared.scad", "module box() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options("/empty", "/libs"), fs);

        Assert.NotNull(Assert.Single(graph.Root.Uses).Target);
    }

    [Fact]
    public void IncludeReadFailure_BecomesSB4001_NeverThrows()
    {
        var fs = new FaultyFileSystem(new InMemoryFileSystem()
            .Add("/proj/main.scad", "include <io-fault.scad>")
            .Add("/proj/io-fault.scad", "module box() cube(1);"));

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Null(Assert.Single(graph.Root.Includes).Target);
    }

    [Fact]
    public void UseReadFailure_AccessDenied_BecomesSB4001_NeverThrows()
    {
        var fs = new FaultyFileSystem(new InMemoryFileSystem()
            .Add("/proj/main.scad", "use <denied.scad>")
            .Add("/proj/denied.scad", "module box() cube(1);"));

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Null(Assert.Single(graph.Root.Uses).Target);
    }

    [Fact]
    public void InvalidRootPath_NeverThrows()
    {
        // A path the real file system rejects: GetFullPath throws, the loader degrades to SB4001.
        LoadGraph graph = SourceLoader.Load("\0bad-path", Options(), DiskFileSystem.Instance);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Empty(graph.Root.Ast.Statements);
    }

    [Fact]
    public void CyclicIncludeGraph_DoesNotInfiniteLoop()
    {
        // A hand-built self-cyclic graph (only constructible by sharing a mutable edge list) — the
        // inliner's splice guard must break it. The real loader never produces this (cycles → SB4002).
        var source = new SourceFile("self.scad", string.Empty);
        var include = new IncludeStatement("self.scad");
        var ast = new ScadFile(source, [include]);
        var edges = new List<IncludeEdge>();
        var loaded = new LoadedFile(source, ast, edges, []);
        edges.Add(new IncludeEdge(include, loaded));
        var graph = new LoadGraph(
            loaded,
            new Dictionary<string, LoadedFile>(StringComparer.Ordinal) { ["self.scad"] = loaded },
            []);
        ISemanticModel model = SemanticAnalyzer.Analyze(graph).Model;

        (ScadFile bundled, _) = Inliner.Bundle(graph, model, BundleOptions.Default);

        Assert.Empty(bundled.Statements); // the cyclic include contributes nothing and terminates
    }

    [Fact]
    public void TransitiveUse_SharedIncludedLibrary_ImportedOnce()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <a.scad>\nuse <b.scad>\nam();\nbm();"),
            ("a.scad", "include <common.scad>\nmodule am() shared();"),
            ("b.scad", "include <common.scad>\nmodule bm() shared();"),
            ("common.scad", "module shared() cube(1);"));

        Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "shared");
        Assert.Contains("am", BundleHelper.TopLevelDeclarationNames(bundled));
        Assert.Contains("bm", BundleHelper.TopLevelDeclarationNames(bundled));
    }

    [Fact]
    public void Use_CallToIncludeImportedLibraryDef_RewritesOnNamespaceCollision()
    {
        // `main` directly calls `shared`, which both libraries expose through `use` via their `include`.
        // The colliding use-imports are namespaced, so the call must rewrite to the binding it resolves
        // to (last-`use`-wins → b's copy). Before the analyzer saw a used lib's include-merged scope the
        // call resolved to nothing, so it was left dangling against a now-renamed definition.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <a.scad>\nuse <b.scad>\nshared();"),
            ("a.scad", "include <ca.scad>"),
            ("ca.scad", "module shared() cube(1);"),
            ("b.scad", "include <cb.scad>"),
            ("cb.scad", "module shared() sphere(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("ca__shared", names);
        Assert.Contains("cb__shared", names);
        var call = Assert.Single(bundled.Statements.OfType<ModuleInstantiation>());
        Assert.Equal("cb__shared", call.Name); // last-`use`-wins → b's namespace
    }

    [Fact]
    public void Use_ImportsFunctionDefinition()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nx = f();"),
            ("lib.scad", "function f() = 1;"));

        Assert.Contains(bundled.Statements, s => s is FunctionDefinition { Name: "f" });
    }

    [Fact]
    public void RootFunctionDefinition_IsPreserved()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "function f() = 1;\nx = f();"));

        Assert.Contains(bundled.Statements, s => s is FunctionDefinition { Name: "f" });
    }

    [Fact]
    public void IncludeVariableReassignment_ReportsSB3003()
    {
        var (_, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>\nx = K;"),
            ("a.scad", "K = 1;"),
            ("b.scad", "K = 2;"));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.VariableReassigned);
    }

    [Fact]
    public void Namespacing_AppliesToCollidingFunctions()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <a.scad>\nuse <b.scad>\nx = shape();"),
            ("a.scad", "function shape() = 1;"),
            ("b.scad", "function shape() = 2;"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("a__shape", names);
        Assert.Contains("b__shape", names);
        Assert.Equal(2, diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed));
    }

    [Fact]
    public void Namespacing_SecondaryClash_AppendsNumericSuffix()
    {
        // Root already owns the name 'gear_a__gear', so namespacing gear_a's colliding 'gear' must
        // disambiguate to 'gear_a__gear_2'.
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "module gear_a__gear() cube(0);\nuse <gear_a.scad>\nuse <gear_b.scad>\ngear();"),
            ("gear_a.scad", "module gear() cube(1);"),
            ("gear_b.scad", "module gear() sphere(1);"));

        Assert.Contains("gear_a__gear_2", BundleHelper.TopLevelDeclarationNames(bundled));
    }

    [Fact]
    public void Namespacing_StemSanitization_HandlesDigitsHyphensAndEmpty()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "module gear() cube(0);\nuse <2lib.scad>\nuse <my-lib.scad>\nuse <.scad>\ngear();"),
            ("2lib.scad", "module gear() sphere(1);"),
            ("my-lib.scad", "module gear() cube(2);"),
            (".scad", "module gear() cylinder(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("_2lib__gear", names);  // digit-leading stem prefixed with '_'
        Assert.Contains("my_lib__gear", names); // hyphen sanitized to '_'
        Assert.Contains("lib__gear", names);    // empty stem falls back to 'lib'
    }
}
