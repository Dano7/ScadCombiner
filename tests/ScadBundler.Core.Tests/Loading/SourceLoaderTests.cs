using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Loading;

/// <summary>
/// The <see cref="SourceLoader"/> over a virtual file system: search-path resolution order, absolute
/// paths, font pass-through, diamond DAGs (load-once), cycle detection (SB4002), not-found (SB4001),
/// and never-throw behavior. Mirrors the OpenSCAD <c>modulecache-tests</c> scenarios in-memory.
/// </summary>
public sealed class SourceLoaderTests
{
    private static BundleOptions Options(params string[] libraryPaths) => new(libraryPaths);

    [Fact]
    public void Include_ResolvesFromIncluderDirectory()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "include <lib.scad>\ncube(1);")
            .Add("/proj/lib.scad", "module box() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Empty(graph.Diagnostics);
        IncludeEdge edge = Assert.Single(graph.Root.Includes);
        Assert.NotNull(edge.Target);
        Assert.Equal("lib.scad", edge.Target!.Source.Path); // display path is the raw include path
    }

    [Fact]
    public void Include_NotFound_ReportsSB4001_AndLeavesEdgeUnresolved()
    {
        var fs = new InMemoryFileSystem().Add("/proj/main.scad", "include <missing.scad>\ncube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Diagnostic diagnostic = Assert.Single(graph.Diagnostics);
        Assert.Equal(DiagnosticCode.IncludeUseNotFound, diagnostic.Code);
        Assert.Null(Assert.Single(graph.Root.Includes).Target);
    }

    [Fact]
    public void Use_Resolves_ToALoadedTarget()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "use <lib.scad>\nbox();")
            .Add("/proj/lib.scad", "module box() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        UseEdge edge = Assert.Single(graph.Root.Uses);
        Assert.NotNull(edge.Target);
        Assert.False(edge.FontPassthrough);
    }

    [Fact]
    public void FontUse_IsPassthrough_NotLoaded()
    {
        var fs = new InMemoryFileSystem().Add("/proj/main.scad", "use <Arial.ttf>\ncube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        UseEdge edge = Assert.Single(graph.Root.Uses);
        Assert.True(edge.FontPassthrough);
        Assert.Null(edge.Target);
        Assert.Empty(graph.Diagnostics);
        Assert.Single(graph.ByAbsolutePath); // only the root was loaded
    }

    [Fact]
    public void DirectCycle_ReportsSB4002_AndTerminates()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/a.scad", "include <b.scad>")
            .Add("/proj/b.scad", "include <a.scad>");

        LoadGraph graph = SourceLoader.Load("/proj/a.scad", Options(), fs);

        Diagnostic diagnostic = Assert.Single(graph.Diagnostics);
        Assert.Equal(DiagnosticCode.CircularReference, diagnostic.Code);
        Assert.Equal(2, graph.ByAbsolutePath.Count); // both files loaded once; no infinite loop
    }

    [Fact]
    public void UseCycle_ReportsSB4002()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/a.scad", "use <b.scad>")
            .Add("/proj/b.scad", "use <a.scad>");

        LoadGraph graph = SourceLoader.Load("/proj/a.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.CircularReference);
        Assert.Equal(2, graph.ByAbsolutePath.Count);
    }

    [Fact]
    public void AbsolutePath_NotFound_ReportsSB4001()
    {
        var fs = new InMemoryFileSystem().Add("/proj/main.scad", "include </nowhere/lib.scad>");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Null(Assert.Single(graph.Root.Includes).Target);
    }

    [Fact]
    public void IndirectCycle_ReportsSB4002()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/a.scad", "include <b.scad>")
            .Add("/proj/b.scad", "include <c.scad>")
            .Add("/proj/c.scad", "include <a.scad>");

        LoadGraph graph = SourceLoader.Load("/proj/a.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.CircularReference);
        Assert.Equal(3, graph.ByAbsolutePath.Count);
    }

    [Fact]
    public void DiamondDag_LoadsSharedFileOnce_NoCycle()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "include <a.scad>\ninclude <b.scad>")
            .Add("/proj/a.scad", "include <common.scad>")
            .Add("/proj/b.scad", "include <common.scad>")
            .Add("/proj/common.scad", "module shared() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Empty(graph.Diagnostics); // a DAG is not a cycle
        Assert.Equal(4, graph.ByAbsolutePath.Count); // common.scad loaded exactly once
    }

    [Fact]
    public void LibraryPath_ConsultedAfterIncluderDirectory()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "use <shared.scad>\ncube(1);")
            .Add("/libs/shared.scad", "module shared() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options("/libs"), fs);

        Assert.NotNull(Assert.Single(graph.Root.Uses).Target);
        Assert.Empty(graph.Diagnostics);
    }

    [Fact]
    public void IncluderDirectory_PreferredOverLibraryPath()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "use <shared.scad>")
            .Add("/proj/shared.scad", "module local_one() cube(1);")
            .Add("/libs/shared.scad", "module library_one() cube(2);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options("/libs"), fs);

        // The includer-directory copy wins (resolution order item 1 before the library path).
        Assert.Equal("/proj/shared.scad", graph.ByAbsolutePath.Keys.Single(k => k.EndsWith("shared.scad", StringComparison.Ordinal)));
    }

    [Fact]
    public void AbsolutePath_UsedDirectly()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "include </shared/lib.scad>")
            .Add("/shared/lib.scad", "module box() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.NotNull(Assert.Single(graph.Root.Includes).Target);
        Assert.Empty(graph.Diagnostics);
    }

    [Fact]
    public void ParseErrorInDependency_Surfaces()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "include <broken.scad>")
            .Add("/proj/broken.scad", "module oops( {"); // malformed parameter list

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code.StartsWith("SB2", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingRoot_NeverThrows_ReportsSB4001()
    {
        var fs = new InMemoryFileSystem();

        LoadGraph graph = SourceLoader.Load("/proj/nope.scad", Options(), fs);

        Assert.Contains(graph.Diagnostics, d => d.Code == DiagnosticCode.IncludeUseNotFound);
        Assert.Empty(graph.Root.Ast.Statements);
    }

    [Fact]
    public void SharedInclude_IsParsedOnce_SameInstance()
    {
        var fs = new InMemoryFileSystem()
            .Add("/proj/main.scad", "include <a.scad>\ninclude <b.scad>")
            .Add("/proj/a.scad", "include <common.scad>")
            .Add("/proj/b.scad", "include <common.scad>")
            .Add("/proj/common.scad", "module shared() cube(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", Options(), fs);

        LoadedFile a = graph.ByAbsolutePath["/proj/a.scad"];
        LoadedFile b = graph.ByAbsolutePath["/proj/b.scad"];
        Assert.Same(a.Includes[0].Target, b.Includes[0].Target); // same cached LoadedFile
    }
}
