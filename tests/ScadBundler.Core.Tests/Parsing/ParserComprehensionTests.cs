using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// The list-comprehension generator battery (Slice 3 §5–§6): <c>for</c>, C-style <c>for</c>,
/// <c>if</c>/<c>else</c>, <c>each</c>, the <c>let</c>-comprehension, their chaining, and the
/// trailing-<c>let</c> ambiguity. Generators are valid only as vector elements; trees are asserted
/// via the deterministic <see cref="AstDump"/>.
/// </summary>
public sealed class ParserComprehensionTests
{
    private static void AssertExpr(string source, string expected) =>
        Assert.Equal(expected, AstDump.Dump(ParseHelper.Expression(source)));

    private static Expression SingleElement(string vectorSource)
    {
        var vector = Assert.IsType<VectorExpression>(ParseHelper.Expression(vectorSource));
        return Assert.Single(vector.Elements);
    }

    [Fact]
    public void For_SingleBinding_YieldsForComprehension()
    {
        AssertExpr(
            "[for (i = [0:5]) i * i]",
            """
            VectorExpression
              Elements:
                ForComprehension
                  Bindings:
                    Binding Name="i"
                      Value: RangeExpression
                        Start: NumberLiteral Value=0 RawText="0"
                        Step: null
                        End: NumberLiteral Value=5 RawText="5"
                  Body: BinaryExpression Operator=Multiply
                    Left: Identifier Name="i"
                    Right: Identifier Name="i"
            """);
    }

    [Fact]
    public void For_MultipleBindings_AreCartesian()
    {
        var comprehension = Assert.IsType<ForComprehension>(SingleElement("[for (i = [0:2], j = [0:2]) [i, j]]"));
        Assert.Equal(2, comprehension.Bindings.Count);
        Assert.Equal("i", comprehension.Bindings[0].Name);
        Assert.Equal("j", comprehension.Bindings[1].Name);
    }

    [Fact]
    public void For_NestedFor_ChainsAsBody()
    {
        var outer = Assert.IsType<ForComprehension>(SingleElement("[for (i = [0:2]) for (j = [0:2]) [i, j]]"));
        var inner = Assert.IsType<ForComprehension>(outer.Body);
        Assert.Equal("j", Assert.Single(inner.Bindings).Name);
    }

    [Fact]
    public void For_WithIfFilter_NestsIfComprehensionAsBody()
    {
        var comprehension = Assert.IsType<ForComprehension>(SingleElement("[for (i = [0:9]) if (i % 2 == 0) i]"));
        var filter = Assert.IsType<IfComprehension>(comprehension.Body);
        Assert.Null(filter.Else); // no else ⇒ filter form
    }

    [Fact]
    public void If_WithElse_SelectsBetweenTwoYields()
    {
        var comprehension = Assert.IsType<ForComprehension>(SingleElement("[for (i = [-1:1]) if (i < 0) -1 else 1]"));
        var select = Assert.IsType<IfComprehension>(comprehension.Body);
        Assert.NotNull(select.Else);
    }

    [Fact]
    public void CStyleFor_DetectedBySemicolons()
    {
        var comprehension = Assert.IsType<ForCComprehension>(SingleElement("[for (a = 0; a < 5; a = a + 1) a]"));
        Assert.Equal("a", Assert.Single(comprehension.Init).Name);
        Assert.Equal("a", Assert.Single(comprehension.Update).Name);
        var condition = Assert.IsType<BinaryExpression>(comprehension.Condition);
        Assert.Equal(BinaryOperator.Less, condition.Operator);
    }

    [Fact]
    public void Each_WrapsItsVectorElement()
    {
        var vector = Assert.IsType<VectorExpression>(ParseHelper.Expression("[each [1, 2, 3], 4]"));
        Assert.Equal(2, vector.Elements.Count);
        var each = Assert.IsType<EachExpression>(vector.Elements[0]);
        Assert.IsType<VectorExpression>(each.Value);
        Assert.IsType<NumberLiteral>(vector.Elements[1]);
    }

    [Fact]
    public void Each_OfAGenerator_NestsTheGenerator()
    {
        var each = Assert.IsType<EachExpression>(SingleElement("[each for (i = [0:2]) i]"));
        Assert.IsType<ForComprehension>(each.Value);
    }

    [Fact]
    public void LetComprehension_WhenBodyIsAGenerator()
    {
        var comprehension = Assert.IsType<LetComprehension>(SingleElement("[let (n = 3) for (i = [0:n]) i]"));
        Assert.Equal("n", Assert.Single(comprehension.Bindings).Name);
        Assert.IsType<ForComprehension>(comprehension.Body);
    }

    [Fact] // E-009 trailing-let rule: a let whose body is a value is a LetExpression element, not a comprehension.
    public void TrailingLet_WhenBodyIsAValue_IsLetExpression()
    {
        var letExpr = Assert.IsType<LetExpression>(SingleElement("[let (a = 1) a]"));
        Assert.Equal("a", Assert.Single(letExpr.Bindings).Name);
        Assert.IsType<Identifier>(letExpr.Body);
    }

    [Fact]
    public void LetComprehension_ChainedLets_NestThroughTheGenerator()
    {
        var outer = Assert.IsType<LetComprehension>(SingleElement("[let (a = 1) let (b = 2) for (i = [0:1]) a + b]"));
        var inner = Assert.IsType<LetComprehension>(outer.Body);
        Assert.IsType<ForComprehension>(inner.Body);
    }

    [Fact]
    public void ParenthesizedGenerator_IsPreservedAroundTheGenerator()
    {
        var paren = Assert.IsType<ParenthesizedExpression>(SingleElement("[(for (i = [0:2]) i)]"));
        Assert.IsType<ForComprehension>(paren.Inner);
    }

    [Fact]
    public void LetComprehension_SeesThroughParensToClassifyTheBody()
    {
        // `let(...) (for ...)` is a LetComprehension even though the body is parenthesized.
        var comprehension = Assert.IsType<LetComprehension>(SingleElement("[let (n = 3) (for (i = [0:n]) i)]"));
        var paren = Assert.IsType<ParenthesizedExpression>(comprehension.Body);
        Assert.IsType<ForComprehension>(paren.Inner);
    }

    [Fact]
    public void PlainVector_StillParses_AfterTheVectorElementUpgrade()
    {
        AssertExpr(
            "[1, 2, 3]",
            """
            VectorExpression
              Elements:
                NumberLiteral Value=1 RawText="1"
                NumberLiteral Value=2 RawText="2"
                NumberLiteral Value=3 RawText="3"
            """);
    }

    [Fact]
    public void Range_StillDetected_WhenFirstElementIsAPlainExpr()
    {
        var range = Assert.IsType<RangeExpression>(ParseHelper.Expression("[0:2:10]"));
        Assert.IsType<NumberLiteral>(range.Start);
        Assert.NotNull(range.Step);
    }

    [Fact]
    public void Comprehension_OutsideVector_IsNotParsedAsAGenerator()
    {
        // `for` in statement position is a ForStatement (Slice 2), never a comprehension. The parser
        // only builds generators inside `[ … ]`; position outside a vector is a semantic concern.
        Statement statement = ParseHelper.Single("for (i = [0:2]) cube(i);");
        Assert.IsType<ForStatement>(statement);
    }
}
