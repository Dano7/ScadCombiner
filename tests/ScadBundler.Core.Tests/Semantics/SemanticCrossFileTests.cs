using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// Cross-file resolution over a <see cref="LoadGraph"/>: <c>use</c> imports definitions (not
/// variables), last-<c>use</c>-wins, own-file declarations shadow used libraries, <c>include</c>
/// merges declarations, and a used callable's own references resolve within its own file (V2). These
/// mirror the OpenSCAD <c>modulecache-tests</c> scenarios (use/used, multipleA/B/common, moduleoverload).
/// </summary>
public sealed class SemanticCrossFileTests
{
    [Fact]
    public void Use_ImportsModule_CallResolvesIntoLibrary()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));
        LoadedFile lib = graph.ByAbsolutePath["lib.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[1];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Module, symbol.Kind);
        Assert.Equal(lib.Source, symbol.File);
        Assert.Same(lib.Ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void Use_ImportsFunction_CallResolvesIntoLibrary()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("A.scad", "use <common.scad>\nmodule A() sphere(F());"),
            ("common.scad", "function F() = 20;"));
        LoadedFile common = graph.ByAbsolutePath["common.scad"];
        var call = SemanticHelper.Find<FunctionCallExpression>(graph.Root.Ast);

        Symbol? symbol = result.Model.Resolve(call.Callee);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Same(common.Ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void TwoUsedLibraries_LastUseWins()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <gear_a.scad>\nuse <gear_b.scad>\ngear();"),
            ("gear_a.scad", "module gear() cube(1);"),
            ("gear_b.scad", "module gear() sphere(1);"));
        LoadedFile gearB = graph.ByAbsolutePath["gear_b.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[2];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(gearB.Ast.Statements[0], symbol.Declaration); // the later `use` wins
    }

    [Fact]
    public void OwnFileDefinition_ShadowsUsedLibrary()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nmodule mymodule() cube(1);\nmymodule();"),
            ("lib.scad", "module mymodule() sphere(1);"));
        var ownDefinition = (ModuleDefinition)graph.Root.Ast.Statements[1];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[2];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(ownDefinition, symbol.Declaration);     // own scope beats the used library
        Assert.Equal(graph.Root.Source, symbol.File);
    }

    [Fact]
    public void Variables_AreNotImportedByUse()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nx = SECRET;"),
            ("lib.scad", "SECRET = 42;"));
        var read = (Identifier)((AssignmentStatement)graph.Root.Ast.Statements[1]).Value;

        Assert.Null(result.Model.Resolve(read)); // SECRET is a library variable, never visible here
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference && d.Message.Contains("SECRET"));
    }

    [Fact]
    public void UsedLibrary_InternalReferences_ResolveWithinItsOwnFile()
    {
        // V2: `box` keeps seeing its own file's WALL; the using file can neither see nor perturb it.
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "WALL = 2;\nmodule box() cube(WALL);"));
        LoadedFile lib = graph.ByAbsolutePath["lib.scad"];
        var wallRead = SemanticHelper.Find<Identifier>(lib.Ast, i => i.Name == "WALL");

        Symbol? symbol = result.Model.Resolve(wallRead);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Same(lib.Ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void Include_MergesDeclarations_IntoIncludingScope()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));
        LoadedFile lib = graph.ByAbsolutePath["lib.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[1];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(lib.Ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void Include_MergedVariable_IsVisible()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "include <lib.scad>\nx = WALL;"),
            ("lib.scad", "WALL = 2;"));
        LoadedFile lib = graph.ByAbsolutePath["lib.scad"];
        var read = (Identifier)((AssignmentStatement)graph.Root.Ast.Statements[1]).Value;

        Symbol? symbol = result.Model.Resolve(read);
        Assert.NotNull(symbol);
        Assert.Same(lib.Ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void FontUse_IsPassthrough_AndDoesNotBreakAnalysis()
    {
        var (_, result) = SemanticHelper.AnalyzeGraph(("main.scad", "use <Arial.ttf>\ncube(1);"));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReferencesTo_AcrossFiles_FindsTheCallSite()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));
        LoadedFile lib = graph.ByAbsolutePath["lib.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[1];
        Symbol box = result.Model.Resolve(call)!;

        Assert.Same(call, Assert.Single(result.Model.ReferencesTo(box)));
    }
}
