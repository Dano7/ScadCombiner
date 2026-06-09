using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// The Slice-5 decision-proving bundle cases (Test-Corpus B-001..B-007) plus dedup, collision
/// strategies, and font pass-through. Assertions are on the bundled AST (presence/absence/rewrite),
/// which is binding now; exact emitted text becomes a golden once Slice 6 locks formatting.
/// </summary>
public sealed class Slice5BundleTests
{
    [Fact]
    public void B001_Include_BringsInEverything_AndExecutesTopLevelGeometry()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <lib.scad>\nbox();"),
            ("lib.scad", "WALL = 2;\nmodule box() cube(WALL);\ncube(99);"));

        Assert.DoesNotContain(bundled.Statements, s => s is IncludeStatement or UseStatement);
        Assert.Contains(bundled.Statements, s => s is AssignmentStatement { Name: "WALL" });
        Assert.Contains(bundled.Statements, s => s is ModuleDefinition { Name: "box" });
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "cube", Arguments: [{ Value: NumberLiteral { Value: 99 } }] });
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "box" });
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void B002_Use_ImportsDefinitionsOnly_PreservesReferencedPrivateConstants()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "$fn = 64;\nWALL = 2;\nUNUSED = 5;\nmodule box() cube(WALL);\ncube(99);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("WALL", names);             // referenced by box → carried as private constant
        Assert.Contains("box", names);
        Assert.DoesNotContain("$fn", names);        // top-level special var dropped
        Assert.DoesNotContain("UNUSED", names);     // unreferenced var dropped
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "box" });
        Assert.DoesNotContain(bundled.Statements, s => s is UseStatement);
        Assert.DoesNotContain(SemanticHelper.Descendants(bundled), n => n is NumberLiteral { Value: 99 }); // cube(99) dropped
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void B003_Assign_NormalizedToLet()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "assign(a = 1, b = 2) translate([a, b, 0]) cube(1);"));

        var let = Assert.IsType<LetStatement>(bundled.Statements[0]);
        Assert.Collection(
            let.Bindings,
            b => Assert.Equal("a", b.Name),
            b => Assert.Equal("b", b.Name));
        Assert.DoesNotContain(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "assign" });

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.AssignNormalized, diagnostic.Code);
        Assert.Equal(1, diagnostic.Span.Start.Line);
        Assert.Equal(1, diagnostic.Span.Start.Column);
    }

    [Fact]
    public void B004_Child_NormalizedToChildren()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "module wrapper() {\n    child();\n    child(1);\n}"));

        List<ModuleInstantiation> children =
        [
            .. SemanticHelper.Descendants(bundled).OfType<ModuleInstantiation>().Where(m => m.Name == "children"),
        ];
        Assert.Equal(2, children.Count);
        Assert.Contains(children, m => m.Arguments is [{ Value: NumberLiteral { Value: 0 } }]); // child() → children(0)
        Assert.Contains(children, m => m.Arguments is [{ Value: NumberLiteral { Value: 1 } }]); // child(1) → children(1)
        Assert.DoesNotContain(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "child" });

        Assert.Equal(
            [DiagnosticCode.ChildNormalized, DiagnosticCode.ChildNormalized],
            diagnostics.Select(d => d.Code));
        Assert.Equal((2, 5), (diagnostics[0].Span.Start.Line, diagnostics[0].Span.Start.Column));
        Assert.Equal((3, 5), (diagnostics[1].Span.Start.Line, diagnostics[1].Span.Start.Column));
    }

    [Fact]
    public void B005_DeprecatedBuiltin_PreservedVerbatim()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "import_stl(\"part.stl\");"));

        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "import_stl" });
        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DeprecatedBuiltinPreserved, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void B006_UseCollision_NamespacesBothLibraries_CallBindsToLastUse()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <gear_a.scad>\nuse <gear_b.scad>\ngear();"),
            ("gear_a.scad", "module gear() cube(1);"),
            ("gear_b.scad", "module gear() sphere(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("gear_a__gear", names);
        Assert.Contains("gear_b__gear", names);
        Assert.DoesNotContain("gear", names);

        var gearA = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "gear_a__gear");
        var gearB = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "gear_b__gear");
        Assert.True(gearA.Body is ModuleInstantiation { Name: "cube" });
        Assert.True(gearB.Body is ModuleInstantiation { Name: "sphere" });

        // The call binds to the last-used library (gear_b), and is rewritten to its namespaced name.
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "gear_b__gear" });
        Assert.DoesNotContain(bundled.Statements, s => s is ModuleInstantiation { Name: "gear" });

        Assert.Equal(2, diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed));
    }

    [Fact]
    public void B007_IncludeDuplicate_LastWins()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "sphere" }); // b (the later include) wins
        Assert.Contains(bundled.Statements, s => s is ModuleInstantiation { Name: "part" });

        Diagnostic redefinition = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DefinitionRedefined, redefinition.Code);
        Assert.Equal("b.scad", redefinition.Span.File.Path); // points at the redefinition (b)
    }

    [Fact]
    public void Diamond_StructurallyIdenticalDefinitions_Deduplicated()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>\nshared();"),
            ("a.scad", "include <common.scad>"),
            ("b.scad", "include <common.scad>"),
            ("common.scad", "module shared() cube(1);"));

        Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "shared");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.DuplicateMerged);
    }

    [Fact]
    public void Diamond_TopLevelGeometry_IsPreservedTwice()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "include <a.scad>\ninclude <b.scad>"),
            ("a.scad", "include <common.scad>"),
            ("b.scad", "include <common.scad>"),
            ("common.scad", "cube(7);"));

        // Duplicated geometry renders twice in OpenSCAD — preserving both is semantic equivalence.
        Assert.Equal(2, bundled.Statements.OfType<ModuleInstantiation>().Count(m => m.Name == "cube"));
    }

    [Fact]
    public void PrefixStrategy_IncludeCollision_KeepsBothNamespaced()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Prefix),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        IReadOnlyList<string> names = BundleHelper.TopLevelDeclarationNames(bundled);
        Assert.Contains("a__part", names);
        Assert.Contains("b__part", names);
        Assert.DoesNotContain("part", names);
        Assert.Equal(2, diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed));

        // The call binds to the later include (LocalScope.cc last-wins), so it rewrites to b's namespace.
        var call = Assert.Single(bundled.Statements.OfType<ModuleInstantiation>());
        Assert.Equal("b__part", call.Name);
    }

    [Fact]
    public void KeepFirstStrategy_IncludeCollision_DropsLater()
    {
        var (bundled, _) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.KeepFirst),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "cube" }); // a (the first) is kept
    }

    [Fact]
    public void KeepLastStrategy_IncludeCollision_KeepsLast_WithSB3004()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.KeepLast),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        var part = Assert.Single(bundled.Statements.OfType<ModuleDefinition>(), m => m.Name == "part");
        Assert.True(part.Body is ModuleInstantiation { Name: "sphere" }); // b (the last) is kept
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
    }

    [Fact]
    public void Assign_WithPositionalArgument_IsNotNormalized()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "assign(1) cube(1);")); // positional arg → not a valid let; left as-is

        Assert.Contains(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "assign" });
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AssignNormalized);
    }

    [Fact]
    public void Assign_WithoutChild_IsNotNormalized()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "assign(a = 1);")); // no child → not rewritten

        Assert.Contains(SemanticHelper.Descendants(bundled), n => n is ModuleInstantiation { Name: "assign" });
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.AssignNormalized);
    }

    [Fact]
    public void ErrorStrategy_Collision_ProducesNoOutput()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            new BundleOptions([], CollisionStrategy.Error),
            ("main.scad", "include <a.scad>\ninclude <b.scad>\npart();"),
            ("a.scad", "module part() cube(1);"),
            ("b.scad", "module part() sphere(1);"));

        Assert.Empty(bundled.Statements);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void FontUse_IsKeptVerbatimInOutput()
    {
        var (bundled, _) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <Arial.ttf>\ntext(\"hi\");"));

        Assert.Contains(bundled.Statements, s => s is UseStatement { RawPath: "Arial.ttf" });
    }

    [Fact]
    public void UseImport_NoCollision_KeepsOriginalName()
    {
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nbox();"),
            ("lib.scad", "module box() cube(1);"));

        Assert.Contains("box", BundleHelper.TopLevelDeclarationNames(bundled));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NameRenamed);
    }

    [Fact]
    public void UsedLibrary_InternalReference_FollowsRenamedPrivateConstant()
    {
        // Root and the used library both define WALL; the library's private constant is namespaced and
        // box keeps seeing its own value (V2 isolation), while root's WALL is untouched.
        var (bundled, diagnostics) = BundleHelper.Bundle(
            null,
            ("main.scad", "use <lib.scad>\nWALL = 99;\nbox();"),
            ("lib.scad", "WALL = 2;\nmodule box() cube(WALL);"));

        Assert.Contains("WALL", BundleHelper.TopLevelDeclarationNames(bundled));     // root's WALL survives
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NameRenamed);     // library WALL namespaced

        var box = bundled.Statements.OfType<ModuleDefinition>().Single(m => m.Name == "box");
        var cubeArg = Assert.IsType<Identifier>(((ModuleInstantiation)box.Body).Arguments[0].Value);
        Assert.StartsWith("lib__WALL", cubeArg.Name); // box's reference rewritten to the namespaced constant

        var rootWall = bundled.Statements.OfType<AssignmentStatement>().Single(a => a.Name == "WALL");
        Assert.True(rootWall.Value is NumberLiteral { Value: 99 }); // root's WALL is the unmodified 99
    }
}
