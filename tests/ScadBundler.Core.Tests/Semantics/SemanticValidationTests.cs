using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// The Slice-4 validation diagnostics: invalid member access (SB3001), comprehension generator
/// outside a vector (SB3002), within-scope reassignment/redefinition (SB3003/SB3004), and the
/// conservative unknown-reference warning (SB3005). Also covers the never-throws contract.
/// </summary>
public sealed class SemanticValidationTests
{
    // ---- SB3001 — invalid vector member (S-001) ----

    [Fact]
    public void InvalidMember_Reports_SB3001()
    {
        SemanticResult result = SemanticHelper.Analyze("v = [1, 2, 3];\nbad = v.w;");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.InvalidMemberAccess);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Invalid member '.w'; only .x, .y, and .z are valid vector components.", diagnostic.Message);
        Assert.Equal(2, diagnostic.Span.Start.Line);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("z")]
    public void ValidMember_NoDiagnostic(string component)
    {
        SemanticResult result = SemanticHelper.Analyze($"v = [1, 2, 3];\nok = v.{component};");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.InvalidMemberAccess);
    }

    // ---- SB3002 — comprehension generator outside a vector (S-002) ----

    [Fact]
    public void Generator_InRangeStart_Reports_SB3002()
    {
        // `[each … : 5]` parses to a RangeExpression whose Start is the generator — a non-vector
        // position the parser accepts, so the semantic guard fires here.
        SemanticResult result = SemanticHelper.Analyze("bad = [each [1, 2, 3] : 5];");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.ComprehensionOutsideVector);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("'each' generator is only valid inside a list comprehension '[ ... ]'.", diagnostic.Message);
    }

    [Fact]
    public void Generator_OutsideVector_ConstructedAst_Reports_SB3002()
    {
        // The parser never produces a generator outside `[ … ]`; this defensive guard matters for the
        // synthesized/rewritten ASTs the Slice-5 inliner builds, so we prove it on a hand-built tree.
        var each = new EachExpression(new VectorExpression([new NumberLiteral(1, "1")]));
        var file = new ScadFile(new SourceFile("c.scad", string.Empty), [new AssignmentStatement("bad", each)]);

        SemanticResult result = SemanticAnalyzer.Analyze(file);
        Assert.Equal(DiagnosticCode.ComprehensionOutsideVector, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void ForGenerator_InRangeStart_Reports_SB3002_WithKeyword()
    {
        SemanticResult result = SemanticHelper.Analyze("bad = [for (i = [0:2]) i : 5];");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.ComprehensionOutsideVector);
        Assert.StartsWith("'for' generator", diagnostic.Message);
    }

    [Fact]
    public void Generator_InsideVector_NoDiagnostic()
    {
        SemanticResult result = SemanticHelper.Analyze("ok = [each [1, 2, 3]];");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ComprehensionOutsideVector);
    }

    [Fact]
    public void NestedAndParenthesizedGenerators_InsideVector_NoDiagnostic()
    {
        SemanticResult result = SemanticHelper.Analyze(
            "ok = [for (i = [0:2]) if (i > 0) each [i, i], (for (j = [0:1]) j)];");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ComprehensionOutsideVector);
    }

    // ---- SB3003 — variable reassigned (last-wins) ----

    [Fact]
    public void Reassignment_Reports_SB3003_NamingFirstLine()
    {
        SemanticResult result = SemanticHelper.Analyze("x = 1;\nx = 2;");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.VariableReassigned);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Variable 'x' was assigned on line 1 but is overwritten; the last assignment wins.", diagnostic.Message);
        Assert.Equal(2, diagnostic.Span.Start.Line);
    }

    [Fact]
    public void Reassignment_ThreeTimes_NamesImmediatelyPriorLine()
    {
        SemanticResult result = SemanticHelper.Analyze("x = 1;\nx = 2;\nx = 3;");
        var messages = result.Diagnostics
            .Where(d => d.Code == DiagnosticCode.VariableReassigned)
            .Select(d => d.Message)
            .ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains("assigned on line 1", messages[0]);
        Assert.Contains("assigned on line 2", messages[1]);
    }

    [Fact]
    public void SingleAssignment_NoDiagnostic()
    {
        SemanticResult result = SemanticHelper.Analyze("x = 1;");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.VariableReassigned);
    }

    // ---- SB3004 — module/function redefined (last-wins) ----

    [Fact]
    public void ModuleRedefinition_Reports_SB3004()
    {
        SemanticResult result = SemanticHelper.Analyze("module m() cube(1);\nmodule m() sphere(1);");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("module 'm' is redefined; the last definition wins.", diagnostic.Message);
        Assert.Equal(2, diagnostic.Span.Start.Line);
    }

    [Fact]
    public void FunctionRedefinition_Reports_SB3004()
    {
        SemanticResult result = SemanticHelper.Analyze("function f() = 1;\nfunction f() = 2;");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
        Assert.Equal("function 'f' is redefined; the last definition wins.", diagnostic.Message);
    }

    [Fact]
    public void SameNameModuleAndFunction_NotADuplicate()
    {
        // Modules and functions occupy distinct namespaces — `module g` and `function g` coexist.
        SemanticResult result = SemanticHelper.Analyze("module g() cube(1);\nfunction g() = 2;");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DefinitionRedefined);
    }

    // ---- SB3005 — conservative unknown reference ----

    [Fact]
    public void UnknownModule_SelfContainedFile_Reports_SB3005()
    {
        SemanticResult result = SemanticHelper.Analyze("nope();");
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Unknown module 'nope'.", diagnostic.Message);
    }

    [Fact]
    public void UnknownFunction_Reports_SB3005()
    {
        SemanticResult result = SemanticHelper.Analyze("x = nope(3);");
        Assert.Equal("Unknown function 'nope'.", Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference).Message);
    }

    [Fact]
    public void UnknownVariable_Reports_SB3005()
    {
        SemanticResult result = SemanticHelper.Analyze("x = missing;");
        Assert.Equal("Unknown variable 'missing'.", Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference).Message);
    }

    [Theory]
    [InlineData("cube(5);")]            // built-in module
    [InlineData("x = sin(30);")]        // built-in function
    [InlineData("x = PI;")]             // built-in constant
    [InlineData("x = $fn;")]            // special variable
    [InlineData("echo(\"hi\");")]       // built-in meta module
    [InlineData("assign(a = 1) cube(a);")] // recognized deprecated module
    public void RecognizedNames_NoUnknown(string source)
    {
        SemanticResult result = SemanticHelper.Analyze(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void UnknownReference_SuppressedWhenFileHasUnresolvedUse()
    {
        // A single-file Analyze cannot see the used library, so it must not flag names that might live
        // there — the conservative rule.
        SemanticResult result = SemanticHelper.Analyze("use <lib.scad>\nfromlib();");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void UnknownReference_EmittedWhenUseIsResolvedButNameAbsent()
    {
        var (_, result) = SemanticHelper.AnalyzeGraph(
            ("main.scad", "use <lib.scad>\nfromlib();\nabsent();"),
            ("lib.scad", "module fromlib() cube(1);"));
        var unknowns = result.Diagnostics.Where(d => d.Code == DiagnosticCode.UnknownReference).ToList();
        Assert.Equal("Unknown module 'absent'.", Assert.Single(unknowns).Message);
    }

    // ---- never throws ----

    [Theory]
    [InlineData("module m( ;")]
    [InlineData("x = ;")]
    [InlineData("bad = v.")]
    [InlineData("for (")]
    [InlineData("function f(a, ) = ;")]
    public void MalformedInput_DoesNotThrow(string source)
    {
        SemanticResult result = SemanticHelper.Analyze(source);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void EmptyFile_DoesNotThrow_AndHasNoDiagnostics()
    {
        SemanticResult result = SemanticHelper.Analyze(string.Empty);
        Assert.Empty(result.Diagnostics);
    }
}
