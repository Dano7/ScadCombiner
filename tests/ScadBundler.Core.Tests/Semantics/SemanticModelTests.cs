using ScadBundler.Core.Ast;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// The per-file declaration queries and the <see cref="ISemanticModel.PrivateConstants"/> reachability
/// closure (§6–§7, worked example §10).
/// </summary>
public sealed class SemanticModelTests
{
    [Fact]
    public void DeclarationQueries_ReturnTopLevelDeclarations_InOrder()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "A = 1;\nmodule m1() cube(1);\nB = 2;\nfunction f1() = 1;\nmodule m2() sphere(1);");

        Assert.Equal(["m1", "m2"], result.Model.Modules(ast.Source).Select(m => m.Name));
        Assert.Equal(["f1"], result.Model.Functions(ast.Source).Select(f => f.Name));
        Assert.Equal(["A", "B"], result.Model.TopLevelVariables(ast.Source).Select(v => v.Name));
    }

    [Fact]
    public void DeclarationQueries_UnknownFile_ReturnEmpty()
    {
        var (_, result) = SemanticHelper.AnalyzeFile("cube(1);");
        var other = new SourceFile("elsewhere.scad", "");

        Assert.Empty(result.Model.Modules(other));
        Assert.Empty(result.Model.Functions(other));
        Assert.Empty(result.Model.TopLevelVariables(other));
        Assert.Empty(result.Model.PrivateConstants(other));
    }

    [Fact]
    public void NestedDefinitions_AreNotTopLevelDeclarations()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module outer() { module inner() cube(1); x = 1; }");
        Assert.Equal(["outer"], result.Model.Modules(ast.Source).Select(m => m.Name));
        Assert.Empty(result.Model.TopLevelVariables(ast.Source)); // `x` is a module-body local
    }

    [Fact]
    public void PrivateConstants_WorkedExample_PullsReferencedAndTransitiveConstants()
    {
        // docs/slices/Slice-4-Semantic.md §10.
        var (ast, result) = SemanticHelper.AnalyzeFile(
            """
            $fn = 64;
            WALL = 2;
            GAP = WALL / 2;
            UNUSED = 5;
            module box() cube([WALL, WALL, GAP]);
            cube(99);
            """,
            "lib.scad");

        IReadOnlyList<AssignmentStatement> constants = result.Model.PrivateConstants(ast.Source);
        Assert.Equal(["WALL", "GAP"], constants.Select(c => c.Name)); // declaration order; GAP is transitive
    }

    [Fact]
    public void PrivateConstants_ReachThroughFunctions()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "K = 3;\nfunction scaled(x) = x * K;\nmodule useIt() cube(scaled(1));",
            "lib.scad");

        Assert.Equal(["K"], result.Model.PrivateConstants(ast.Source).Select(c => c.Name));
    }

    [Fact]
    public void PrivateConstants_ExcludeUnreferencedGeometryAndSpecials()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "$fn = 64;\nUNUSED = 5;\nmodule plain() cube(1);\ntranslate([1, 0, 0]) sphere(2);",
            "lib.scad");

        Assert.Empty(result.Model.PrivateConstants(ast.Source));
    }

    [Fact]
    public void PrivateConstants_ParameterDefault_PullsConstant()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "DEF = 7;\nmodule m(w = DEF) cube(w);",
            "lib.scad");

        Assert.Equal(["DEF"], result.Model.PrivateConstants(ast.Source).Select(c => c.Name));
    }

    [Fact]
    public void PrivateConstants_DoesNotIncludeLocalShadow()
    {
        // `box` has a parameter WALL that shadows the top-level WALL, so the constant is NOT referenced.
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "WALL = 2;\nmodule box(WALL) cube(WALL);",
            "lib.scad");

        Assert.Empty(result.Model.PrivateConstants(ast.Source));
    }
}
