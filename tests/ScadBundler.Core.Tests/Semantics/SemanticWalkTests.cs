using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// Exercises the resolver over every statement and expression form, confirming that references inside
/// rich constructs bind correctly (top-level → symbol, local → null) and that the scope walk reaches
/// each node. Complements the focused rule tests with breadth.
/// </summary>
public sealed class SemanticWalkTests
{
    private static Symbol? Resolve(SemanticResult result, AstNode node) => result.Model.Resolve(node);

    [Fact]
    public void RichExpressionForms_ResolveTopLevelAndLocals()
    {
        // Covers range-with-step, binary, unary, conditional, index, parenthesized, function literal.
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "K = 10;\nr = [0 : 2 : K];\nf = function (i) (i > 0 ? -i : ~i) + [1, 2, 3][i];");

        Symbol? k = Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "K"));
        Assert.Equal(SymbolKind.Variable, k?.Kind);
        Assert.Null(Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "i"))); // parameter
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void AllComprehensionForms_InsideVector_ResolveCleanly()
    {
        // C-style for, if/else, let-comprehension (generator body), and each — all valid vector elements.
        var (ast, result) = SemanticHelper.AnalyzeFile(
            """
            K = 5;
            xs = [
                for (a = 0; a < K; a = a + 1) a,
                if (K > 0) K else -K,
                let (b = K) each [b],
                each [K]
            ];
            """);

        Symbol? k = Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "K"));
        Assert.Equal(SymbolKind.Variable, k?.Kind);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ComprehensionOutsideVector);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);

        // The comprehension binding `a` is local.
        var forC = SemanticHelper.Find<ForCComprehension>(ast);
        var conditionRead = (Identifier)((BinaryExpression)forC.Condition).Left;
        Assert.Null(Resolve(result, conditionRead));
    }

    [Fact]
    public void StatementForms_IntersectionForLetIf_WalkAndResolve()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "K = 3;\nintersection_for (i = [0:K]) cube(i);\nlet (j = K) sphere(j);\nif (K > 0) cube(K); else sphere(K);");

        Symbol? k = Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "K"));
        Assert.Equal(SymbolKind.Variable, k?.Kind);
        Assert.Null(Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "i"))); // for binding
        Assert.Null(Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "j"))); // let binding
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void AssertAndEcho_ExpressionsWithBody_Resolve()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("K = 1;\np = assert(K > 0) K;\nq = echo(\"v\", K) K;");

        Assert.IsType<AssertExpression>(((AssignmentStatement)ast.Statements[1]).Value);
        Assert.IsType<EchoExpression>(((AssignmentStatement)ast.Statements[2]).Value);
        Symbol? k = Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "K"));
        Assert.Equal(SymbolKind.Variable, k?.Kind);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void ModuleBodyLocals_HoistThroughBlocksIfAndGeometry_ShadowTopLevel()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            """
            K = 9;
            module host() {
                function loc() = 1;
                module nested() cube(1);
                if (true) a = 1; else d = 3;
                union() { b = 2; }
                nested();
                c = loc();
                cube([a, b, c, d, K]);
            }
            """);

        // Locals bound in the module body (incl. via if-branch and geometry block) shadow nothing
        // renameable; only the top-level K resolves to a symbol.
        Assert.Null(Resolve(result, SemanticHelper.Find<ModuleInstantiation>(ast, m => m.Name == "nested")));
        var locCall = SemanticHelper.Find<FunctionCallExpression>(ast, c => c.Callee is Identifier { Name: "loc" });
        Assert.Null(Resolve(result, locCall.Callee));
        Assert.Null(Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "a"))); // if-then local
        Assert.Null(Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "b"))); // geometry-block local
        Assert.Null(Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "d"))); // if-else local
        Assert.Equal(SymbolKind.Variable, Resolve(result, SemanticHelper.Find<Identifier>(ast, n => n.Name == "K"))?.Kind);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void DiamondInclude_RevisitsCommonOnce_AndResolves()
    {
        var (graph, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "include <a.scad>\ninclude <b.scad>\nthing();"),
            ("a.scad", "include <common.scad>"),
            ("b.scad", "include <common.scad>"),
            ("common.scad", "module thing() cube(1);"));
        var call = (ModuleInstantiation)graph.Root.Ast.Statements[2];

        Symbol? symbol = result.Model.Resolve(call);
        Assert.NotNull(symbol);
        Assert.Same(graph.ByAbsolutePath["common.scad"].Ast.Statements[0], symbol.Declaration);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }
}
