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
    public void Use_ExposesDefinitionsTheLibraryIncludes()
    {
        // `use <lib>` sees `lib`'s `include`-merged scope: OpenSCAD splices `include`d defs into the
        // library's own scope at parse time, so `helper` (pulled into lib by its include) is callable
        // through the `use` — matching FileContext::lookup_local_module and the inliner's import set.
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nhelper();"),
            ("lib.scad", "include <helpers.scad>"),
            ("helpers.scad", "module helper() cube(1);"));
        LoadedFile helpers = graph.ByAbsolutePath["helpers.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[1];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(helpers.Ast.Statements[0], symbol.Declaration);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void Use_DoesNotExposeDefinitionsTheLibraryUses()
    {
        // `use` is non-transitive: `lib` only `use`s `deep`, so `deep`'s defs are not in `lib`'s own
        // scope and stay invisible to `main` (FileContext consults a used lib's scope, never its usedlibs).
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\ndeep();"),
            ("lib.scad", "use <deep.scad>"),
            ("deep.scad", "module deep() cube(1);"));
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[1];

        Assert.Null(result.Model.Resolve(call));
        Assert.Contains(
            result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownReference && d.Message.Contains("deep"));
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
    public void Include_SiblingIncludedDefinition_IsVisibleToLaterInclude()
    {
        // OpenSCAD `include` splices every included file into the root's one flat scope, so a file
        // included after another sees the earlier sibling's definitions even though it includes nothing
        // itself — the BOSL2 `include <std.scad>` then `include <gears.scad>` pattern, where gears.scad
        // freely calls std.scad's functions without including it.
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "include <defs.scad>\ninclude <consumer.scad>"),
            ("defs.scad", "function helper() = 1;"),
            ("consumer.scad", "result = helper();"));
        LoadedFile defs = graph.ByAbsolutePath["defs.scad"];
        LoadedFile consumer = graph.ByAbsolutePath["consumer.scad"];
        var call = (FunctionCallExpression)((AssignmentStatement)consumer.Ast.Statements[0]).Value;

        Symbol? symbol = result.Model.Resolve(call.Callee);
        Assert.NotNull(symbol);
        Assert.Same(defs.Ast.Statements[0], symbol.Declaration); // binds to the sibling-included def
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void Include_HoistsUseImports_AcrossTheIsland()
    {
        // A `use` inside an `include`d file is hoisted into the includer's flat scope (include is
        // textual), so a sibling-included file resolves through it. Mirrors BOSL2's
        // `color.scad: use <builtins.scad>` becoming visible to `attachments.scad`'s `_color()` once
        // std.scad includes both.
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "include <wrap.scad>\ninclude <other.scad>"),
            ("wrap.scad", "use <prims.scad>"),
            ("other.scad", "prim();"),
            ("prims.scad", "module prim() cube(1);"));
        LoadedFile prims = graph.ByAbsolutePath["prims.scad"];
        var call = (ModuleInstantiation)graph.ByAbsolutePath["other.scad"].Ast.Statements[0];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(prims.Ast.Statements[0], symbol.Declaration);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
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
    public void Include_DuplicateAcrossIncludes_BindsToLaterInclude()
    {
        // `include` is flat-scope last-wins (LocalScope.cc / corpus B-007): when two included files
        // define `part`, a reference binds to the later include, not the earlier one.
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));
        LoadedFile b = graph.ByAbsolutePath["b.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[2];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(b.Ast.Statements[0], symbol.Declaration); // the later include wins
        Assert.Equal(b.Source, symbol.File);
    }

    [Fact]
    public void Include_AfterOwnDefinition_OverridesItByDocumentOrder()
    {
        // The include sits after main's own `part`, so in flattened document order the included `part`
        // is the later definition and wins — own scope does not unconditionally shadow includes.
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "module part() cube(1);\ninclude <a.scad>\npart();"),
            ("a.scad", "module part() sphere(1);"));
        LoadedFile a = graph.ByAbsolutePath["a.scad"];
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[2];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(a.Ast.Statements[0], symbol.Declaration); // later (included) definition wins
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
