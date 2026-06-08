using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Module-instantiation coverage: name-recognition into dedicated control-flow nodes, generic
/// instantiations for echo/assert/children/assign, child chaining, modifier stacking, and the
/// AST-Reference §14.1/§14.2 and Slice-2 §12 worked examples.
/// </summary>
public sealed class ParserModuleInstantiationTests
{
    private static void AssertStatement(string source, string expected) =>
        Assert.Equal(expected, AstDump.Dump(ParseHelper.Single(source)));

    [Fact] // AST-Reference §14.1
    public void Instantiation_WithPositionalAndNamedArguments()
    {
        AssertStatement(
            "cube([10, 20, 30], center = true);",
            """
            ModuleInstantiation Name="cube"
              Arguments:
                Argument
                  Value: VectorExpression
                    Elements:
                      NumberLiteral Value=10 RawText="10"
                      NumberLiteral Value=20 RawText="20"
                      NumberLiteral Value=30 RawText="30"
                Argument Name="center"
                  Value: BooleanLiteral Value=true
              Child: null
            """);
    }

    [Fact] // AST-Reference §14.2
    public void TransformChain_NestsAsChildren()
    {
        AssertStatement(
            "translate([0, 0, 5]) rotate([0, 0, 45]) cube(10);",
            """
            ModuleInstantiation Name="translate"
              Arguments:
                Argument
                  Value: VectorExpression
                    Elements:
                      NumberLiteral Value=0 RawText="0"
                      NumberLiteral Value=0 RawText="0"
                      NumberLiteral Value=5 RawText="5"
              Child: ModuleInstantiation Name="rotate"
                Arguments:
                  Argument
                    Value: VectorExpression
                      Elements:
                        NumberLiteral Value=0 RawText="0"
                        NumberLiteral Value=0 RawText="0"
                        NumberLiteral Value=45 RawText="45"
                Child: ModuleInstantiation Name="cube"
                  Arguments:
                    Argument
                      Value: NumberLiteral Value=10 RawText="10"
                  Child: null
            """);
    }

    [Fact] // Slice-2 §12
    public void For_RecognizedByName_WithRangeBindingAndChildChain()
    {
        AssertStatement(
            "for (i = [0:2:10]) translate([i, 0, 0]) cube(1);",
            """
            ForStatement
              Bindings:
                Binding Name="i"
                  Value: RangeExpression
                    Start: NumberLiteral Value=0 RawText="0"
                    Step: NumberLiteral Value=2 RawText="2"
                    End: NumberLiteral Value=10 RawText="10"
              Body: ModuleInstantiation Name="translate"
                Arguments:
                  Argument
                    Value: VectorExpression
                      Elements:
                        Identifier Name="i"
                        NumberLiteral Value=0 RawText="0"
                        NumberLiteral Value=0 RawText="0"
                Child: ModuleInstantiation Name="cube"
                  Arguments:
                    Argument
                      Value: NumberLiteral Value=1 RawText="1"
                  Child: null
            """);
    }

    [Fact] // P-001
    public void ModifierStacking_IsOuterToInner()
    {
        AssertStatement(
            "#%cube(1);",
            """
            ModuleInstantiation Name="cube" Modifiers=[Highlight, Background]
              Arguments:
                Argument
                  Value: NumberLiteral Value=1 RawText="1"
              Child: null
            """);
    }

    [Theory]
    [InlineData("*a();", InstantiationModifier.Disable)]
    [InlineData("!a();", InstantiationModifier.Root)]
    [InlineData("#a();", InstantiationModifier.Highlight)]
    [InlineData("%a();", InstantiationModifier.Background)]
    public void EachModifier_MapsToItsEnum(string source, InstantiationModifier expected)
    {
        var instantiation = Assert.IsType<ModuleInstantiation>(ParseHelper.Single(source));
        Assert.Equal(expected, Assert.Single(instantiation.Modifiers));
    }

    [Fact]
    public void Let_RecognizedByName_IntoLetStatement()
    {
        var let = Assert.IsType<LetStatement>(ParseHelper.Single("let (x = 1) cube(x);"));
        Binding binding = Assert.Single(let.Bindings);
        Assert.Equal("x", binding.Name);
    }

    [Fact]
    public void IntersectionFor_RecognizedByName()
    {
        var node = Assert.IsType<IntersectionForStatement>(
            ParseHelper.Single("intersection_for (i = [0:2]) cube(i);"));
        Assert.Equal("i", Assert.Single(node.Bindings).Name);
    }

    [Theory]
    [InlineData("echo(\"hi\");", "echo")]
    [InlineData("assert(x > 0);", "assert")]
    [InlineData("assign(a = 1) cube(a);", "assign")]
    [InlineData("children(0);", "children")]
    [InlineData("each([1]);", "each")]
    public void EchoAssertAssignChildren_AreGenericInstantiations(string source, string name)
    {
        var instantiation = Assert.IsType<ModuleInstantiation>(ParseHelper.Single(source));
        Assert.Equal(name, instantiation.Name);
    }

    [Fact]
    public void BracedChildren_BecomeABlockChild()
    {
        var union = Assert.IsType<ModuleInstantiation>(ParseHelper.Single("union() { a(); b(); }"));
        var block = Assert.IsType<BlockStatement>(union.Child);
        Assert.Equal(2, block.Statements.Count);
    }

    [Fact]
    public void TerminatingSemicolon_LeavesChildNull()
    {
        var instantiation = Assert.IsType<ModuleInstantiation>(ParseHelper.Single("sphere(1);"));
        Assert.Null(instantiation.Child);
    }
}
