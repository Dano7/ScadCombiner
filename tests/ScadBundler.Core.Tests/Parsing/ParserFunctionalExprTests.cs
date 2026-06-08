using ScadBundler.Core.Ast;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// The keyword-prefixed <c>expr</c> forms (Slice 3 §4): the anonymous <c>function</c> literal and the
/// <c>let</c>/<c>assert</c>/<c>echo</c> expressions, including <c>expr_or_empty</c> bodies, right-greedy
/// nesting, and immediately-invoked literals. These forms sit at the top of <c>expr</c>, so they nest
/// anywhere an expression is expected.
/// </summary>
public sealed class ParserFunctionalExprTests
{
    [Fact]
    public void FunctionLiteral_OneParameter()
    {
        AssertExpr(
            "function (x) x * 2",
            """
            FunctionLiteral
              Parameters:
                Parameter Name="x"
              Body: BinaryExpression Operator=Multiply
                Left: Identifier Name="x"
                Right: NumberLiteral Value=2 RawText="2"
            """);
    }

    [Fact]
    public void FunctionLiteral_ZeroParameters()
    {
        var literal = Assert.IsType<FunctionLiteral>(ParseHelper.Expression("function () 42"));
        Assert.Empty(literal.Parameters);
        Assert.IsType<NumberLiteral>(literal.Body);
    }

    [Fact]
    public void FunctionLiteral_ManyParameters_WithDefaults()
    {
        var literal = Assert.IsType<FunctionLiteral>(ParseHelper.Expression("function (a, b = 1, c = a + b) a + b + c"));
        Assert.Equal(3, literal.Parameters.Count);
        Assert.Null(literal.Parameters[0].DefaultValue);
        Assert.NotNull(literal.Parameters[1].DefaultValue);
        Assert.NotNull(literal.Parameters[2].DefaultValue);
    }

    [Fact]
    public void FunctionLiteral_BodyIsRightGreedy_AbsorbsTheCall()
    {
        // `function (x) x(5)` ⇒ body is the call `x(5)` (right-greedy), not an immediate invocation.
        var literal = Assert.IsType<FunctionLiteral>(ParseHelper.Expression("function (x) x(5)"));
        Assert.IsType<FunctionCallExpression>(literal.Body);
    }

    [Fact]
    public void FunctionLiteral_ImmediatelyInvoked_RequiresParentheses()
    {
        // `(function (x) x)(5)` ⇒ a call whose callee is the parenthesized literal (Slice 2 postfix).
        var call = Assert.IsType<FunctionCallExpression>(ParseHelper.Expression("(function (x) x)(5)"));
        var paren = Assert.IsType<ParenthesizedExpression>(call.Callee);
        Assert.IsType<FunctionLiteral>(paren.Inner);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void FunctionLiteral_AsFunctionDefinitionBody()
    {
        var definition = Assert.IsType<FunctionDefinition>(ParseHelper.Single("function adder(n) = function (x) x + n;"));
        Assert.IsType<FunctionLiteral>(definition.Body);
    }

    [Fact]
    public void LetExpression_BindsThenEvaluatesBody()
    {
        AssertExpr(
            "let (a = 1, b = 2) a + b",
            """
            LetExpression
              Bindings:
                Binding Name="a"
                  Value: NumberLiteral Value=1 RawText="1"
                Binding Name="b"
                  Value: NumberLiteral Value=2 RawText="2"
              Body: BinaryExpression Operator=Add
                Left: Identifier Name="a"
                Right: Identifier Name="b"
            """);
    }

    [Fact]
    public void LetExpression_BodyMayBeATernary()
    {
        // The let body is `expr`, so it absorbs the whole ternary; the let is not a ternary condition.
        var letExpr = Assert.IsType<LetExpression>(ParseHelper.Expression("let (a = 1) a ? 2 : 3"));
        Assert.IsType<ConditionalExpression>(letExpr.Body);
    }

    [Fact]
    public void AssertExpression_WithBody()
    {
        var assertExpr = Assert.IsType<AssertExpression>(ParseHelper.Expression("assert(n > 0) n"));
        Assert.Single(assertExpr.Arguments);
        Assert.IsType<Identifier>(assertExpr.Body);
    }

    [Fact] // E-012 expr_or_empty: no trailing expression ⇒ Body is null.
    public void AssertExpression_WithoutBody_HasNullBody()
    {
        var assertExpr = Assert.IsType<AssertExpression>(ParseHelper.Expression("assert(n > 0)"));
        Assert.Single(assertExpr.Arguments);
        Assert.Null(assertExpr.Body);
    }

    [Fact]
    public void AssertExpression_WithNamedMessage()
    {
        var assertExpr = Assert.IsType<AssertExpression>(ParseHelper.Expression("assert(n > 0, message = \"bad\") n"));
        Assert.Equal(2, assertExpr.Arguments.Count);
        Assert.Equal("message", assertExpr.Arguments[1].Name);
    }

    [Fact]
    public void EchoExpression_WithBody()
    {
        var echoExpr = Assert.IsType<EchoExpression>(ParseHelper.Expression("echo(\"x\", n) n + 1"));
        Assert.Equal(2, echoExpr.Arguments.Count);
        Assert.IsType<BinaryExpression>(echoExpr.Body);
    }

    [Fact]
    public void EchoExpression_WithoutBody_HasNullBody()
    {
        var echoExpr = Assert.IsType<EchoExpression>(ParseHelper.Expression("echo(\"trace\")"));
        Assert.Null(echoExpr.Body);
    }

    [Fact]
    public void KeywordForm_NestsAsAnArgument()
    {
        // A keyword-prefixed form is a value, so it nests wherever an expression is expected.
        var call = Assert.IsType<FunctionCallExpression>(ParseHelper.Expression("f(let (x = 1) x)"));
        Assert.IsType<LetExpression>(Assert.Single(call.Arguments).Value);
    }

    [Fact]
    public void KeywordForm_NestsAsAVectorElement()
    {
        var vector = Assert.IsType<VectorExpression>(ParseHelper.Expression("[assert(n > 0) n, echo(n)]"));
        Assert.IsType<AssertExpression>(vector.Elements[0]);
        Assert.IsType<EchoExpression>(vector.Elements[1]);
    }

    [Fact]
    public void KeywordForm_IsNotABinaryOperand()
    {
        // `1 + let(...) ...` is invalid: keyword forms sit at the top of `expr`, never as an operand.
        Assert.NotEmpty(ParseHelper.ParseTokensOnly("x = 1 + let (a = 1) a;").Diagnostics);
    }

    private static void AssertExpr(string source, string expected) =>
        Assert.Equal(expected, AstDump.Dump(ParseHelper.Expression(source)));
}
