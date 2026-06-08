using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// The expression precedence/associativity battery (Test-Corpus E-001..E-008) plus postfix chains,
/// primaries, vectors, and ranges. Trees are asserted via the deterministic <see cref="AstDump"/>.
/// </summary>
public sealed class ParserExpressionTests
{
    private static void AssertExpr(string source, string expected) =>
        Assert.Equal(expected, AstDump.Dump(ParseHelper.Expression(source)));

    [Fact] // E-001
    public void Multiplicative_BindsTighterThan_Additive()
    {
        AssertExpr(
            "a + b * c",
            """
            BinaryExpression Operator=Add
              Left: Identifier Name="a"
              Right: BinaryExpression Operator=Multiply
                Left: Identifier Name="b"
                Right: Identifier Name="c"
            """);
    }

    [Fact] // E-002
    public void And_BindsTighterThan_Or()
    {
        AssertExpr(
            "a || b && c",
            """
            BinaryExpression Operator=Or
              Left: Identifier Name="a"
              Right: BinaryExpression Operator=And
                Left: Identifier Name="b"
                Right: Identifier Name="c"
            """);
    }

    [Fact] // E-003
    public void Ternary_IsRightAssociative()
    {
        AssertExpr(
            "a ? b : c ? d : e",
            """
            ConditionalExpression
              Condition: Identifier Name="a"
              Then: Identifier Name="b"
              Else: ConditionalExpression
                Condition: Identifier Name="c"
                Then: Identifier Name="d"
                Else: Identifier Name="e"
            """);
    }

    [Fact] // E-004
    public void Power_IsRightAssociative()
    {
        AssertExpr(
            "2 ^ 3 ^ 2",
            """
            BinaryExpression Operator=Power
              Left: NumberLiteral Value=2 RawText="2"
              Right: BinaryExpression Operator=Power
                Left: NumberLiteral Value=3 RawText="3"
                Right: NumberLiteral Value=2 RawText="2"
            """);
    }

    [Fact] // E-005
    public void Power_BindsTighterThan_UnaryMinus()
    {
        AssertExpr(
            "-2 ^ 2",
            """
            UnaryExpression Operator=Negate
              Operand: BinaryExpression Operator=Power
                Left: NumberLiteral Value=2 RawText="2"
                Right: NumberLiteral Value=2 RawText="2"
            """);
    }

    [Fact] // E-006
    public void Power_RightOperand_MayBeUnary()
    {
        AssertExpr(
            "2 ^ -1",
            """
            BinaryExpression Operator=Power
              Left: NumberLiteral Value=2 RawText="2"
              Right: UnaryExpression Operator=Negate
                Operand: NumberLiteral Value=1 RawText="1"
            """);
    }

    [Fact] // E-007
    public void Unary_Stacks_RightAssociative()
    {
        AssertExpr(
            "!!a",
            """
            UnaryExpression Operator=Not
              Operand: UnaryExpression Operator=Not
                Operand: Identifier Name="a"
            """);
    }

    [Fact] // E-008
    public void BitwiseAnd_TighterThanOr_BothLooserThanArithmetic()
    {
        AssertExpr(
            "a | b & c + d",
            """
            BinaryExpression Operator=BitwiseOr
              Left: Identifier Name="a"
              Right: BinaryExpression Operator=BitwiseAnd
                Left: Identifier Name="b"
                Right: BinaryExpression Operator=Add
                  Left: Identifier Name="c"
                  Right: Identifier Name="d"
            """);
    }

    [Fact]
    public void Comparison_IsLooserThanShift_AndArithmetic()
    {
        // `a < b << c` → `<`(40) looser than `<<`(70): a < (b << c)
        AssertExpr(
            "a < b << c",
            """
            BinaryExpression Operator=Less
              Left: Identifier Name="a"
              Right: BinaryExpression Operator=ShiftLeft
                Left: Identifier Name="b"
                Right: Identifier Name="c"
            """);
    }

    [Fact]
    public void PostfixChain_MemberIndexCall_NestsLeftToRight()
    {
        AssertExpr(
            "a.x[0](1)",
            """
            FunctionCallExpression
              Callee: IndexExpression
                Target: MemberExpression Member="x"
                  Target: Identifier Name="a"
                Index: NumberLiteral Value=0 RawText="0"
              Arguments:
                Argument
                  Value: NumberLiteral Value=1 RawText="1"
            """);
    }

    [Fact]
    public void NestedParentheses_ArePreserved()
    {
        AssertExpr(
            "((a))",
            """
            ParenthesizedExpression
              Inner: ParenthesizedExpression
                Inner: Identifier Name="a"
            """);
    }

    [Fact]
    public void EmptyVector_HasNoElements()
    {
        AssertExpr(
            "[]",
            """
            VectorExpression
              Elements: []
            """);
    }

    [Fact]
    public void Vector_WithTrailingComma_DropsTheEmptySlot()
    {
        AssertExpr(
            "[1, 2,]",
            """
            VectorExpression
              Elements:
                NumberLiteral Value=1 RawText="1"
                NumberLiteral Value=2 RawText="2"
            """);
    }

    [Fact]
    public void Range_TwoPart_HasNullStep()
    {
        AssertExpr(
            "[0:10]",
            """
            RangeExpression
              Start: NumberLiteral Value=0 RawText="0"
              Step: null
              End: NumberLiteral Value=10 RawText="10"
            """);
    }

    [Fact]
    public void Range_ThreePart_HasStep()
    {
        AssertExpr(
            "[0:2:10]",
            """
            RangeExpression
              Start: NumberLiteral Value=0 RawText="0"
              Step: NumberLiteral Value=2 RawText="2"
              End: NumberLiteral Value=10 RawText="10"
            """);
    }

    [Fact]
    public void Literals_DecodeValueAndPreserveRawText()
    {
        var hex = Assert.IsType<NumberLiteral>(ParseHelper.Expression("0xFF"));
        Assert.Equal(255, hex.Value);
        Assert.Equal("0xFF", hex.RawText);

        var sci = Assert.IsType<NumberLiteral>(ParseHelper.Expression("2.5e3"));
        Assert.Equal(2500, sci.Value);
        Assert.Equal("2.5e3", sci.RawText);

        var str = Assert.IsType<StringLiteral>(ParseHelper.Expression("\"a\\tb\""));
        Assert.Equal("a\tb", str.Value);

        Assert.IsType<BooleanLiteral>(ParseHelper.Expression("true"));
        Assert.IsType<UndefLiteral>(ParseHelper.Expression("undef"));
    }

    [Fact]
    public void UnaryPlusAndBitwiseNot_AreModeled()
    {
        var plus = Assert.IsType<UnaryExpression>(ParseHelper.Expression("+a"));
        Assert.Equal(UnaryOperator.Plus, plus.Operator);

        var not = Assert.IsType<UnaryExpression>(ParseHelper.Expression("~a"));
        Assert.Equal(UnaryOperator.BitwiseNot, not.Operator);
    }

    [Theory]
    [InlineData("a == b", BinaryOperator.Equal)]
    [InlineData("a != b", BinaryOperator.NotEqual)]
    [InlineData("a < b", BinaryOperator.Less)]
    [InlineData("a <= b", BinaryOperator.LessEqual)]
    [InlineData("a > b", BinaryOperator.Greater)]
    [InlineData("a >= b", BinaryOperator.GreaterEqual)]
    [InlineData("a && b", BinaryOperator.And)]
    [InlineData("a || b", BinaryOperator.Or)]
    [InlineData("a & b", BinaryOperator.BitwiseAnd)]
    [InlineData("a | b", BinaryOperator.BitwiseOr)]
    [InlineData("a << b", BinaryOperator.ShiftLeft)]
    [InlineData("a >> b", BinaryOperator.ShiftRight)]
    [InlineData("a + b", BinaryOperator.Add)]
    [InlineData("a - b", BinaryOperator.Subtract)]
    [InlineData("a * b", BinaryOperator.Multiply)]
    [InlineData("a / b", BinaryOperator.Divide)]
    [InlineData("a % b", BinaryOperator.Modulo)]
    [InlineData("a ^ b", BinaryOperator.Power)]
    public void EveryBinaryOperator_MapsToItsEnum(string source, BinaryOperator expected)
    {
        var binary = Assert.IsType<BinaryExpression>(ParseHelper.Expression(source));
        Assert.Equal(expected, binary.Operator);
    }

    [Fact]
    public void FalseLiteral_Parses()
    {
        var literal = Assert.IsType<BooleanLiteral>(ParseHelper.Expression("false"));
        Assert.False(literal.Value);
    }

    [Fact]
    public void TrailingComma_InArguments_IsAllowed()
    {
        var call = Assert.IsType<FunctionCallExpression>(ParseHelper.Expression("f(1,)"));
        Assert.Single(call.Arguments);
    }
}
