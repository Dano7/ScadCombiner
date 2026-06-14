namespace ScadBundler.Core.Ast;

/// <summary>Base of all expression nodes.</summary>
public abstract record Expression : AstNode;

/// <summary>
/// A numeric literal. <see cref="RawText"/> preserves the author's lexical form (<c>"1"</c>,
/// <c>"1.0"</c>, <c>"1e3"</c>, <c>".5"</c>, <c>"0xFF"</c>); <see cref="Value"/> is the parsed double.
/// </summary>
/// <param name="Value">The parsed numeric value.</param>
/// <param name="RawText">The verbatim source lexeme.</param>
public sealed record NumberLiteral(double Value, string RawText) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// A string literal. <see cref="Value"/> is the decoded string (escapes resolved);
/// <see cref="RawText"/> is the verbatim source lexeme including quotes.
/// </summary>
/// <param name="Value">The decoded string value.</param>
/// <param name="RawText">The verbatim source lexeme including surrounding quotes.</param>
public sealed record StringLiteral(string Value, string RawText) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>true</c> or <c>false</c>.</summary>
/// <param name="Value">The boolean value.</param>
public sealed record BooleanLiteral(bool Value) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>undef</c>.</summary>
public sealed record UndefLiteral() : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// A variable or function/module name reference. Special variables (<c>$fn</c>, <c>$t</c>, …) are
/// identifiers whose <see cref="Name"/> begins with <c>$</c>.
/// </summary>
/// <param name="Name">The referenced name.</param>
public sealed record Identifier(string Name) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>[Elements]</c>. Elements may include comprehension generators, which are valid only inside a
/// vector context (enforced by the semantic pass). An empty vector <c>[]</c> has zero elements.
/// </summary>
/// <param name="Elements">The vector elements.</param>
public sealed record VectorExpression(IReadOnlyList<Expression> Elements) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>[Start : End]</c> (Step null) or <c>[Start : Step : End]</c>.</summary>
/// <param name="Start">The range start.</param>
/// <param name="Step">The range step, or <c>null</c> when omitted.</param>
/// <param name="End">The range end.</param>
public sealed record RangeExpression(
    Expression Start,
    Expression? Step,
    Expression End) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>A binary operation <c>Left Operator Right</c>.</summary>
/// <param name="Operator">The operator.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
public sealed record BinaryExpression(
    BinaryOperator Operator,
    Expression Left,
    Expression Right) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// A prefix unary operation. Note <c>-5</c> parses as <c>UnaryExpression(Negate, NumberLiteral 5)</c>,
/// not as a negative literal.
/// </summary>
/// <param name="Operator">The unary operator.</param>
/// <param name="Operand">The operand.</param>
public sealed record UnaryExpression(
    UnaryOperator Operator,
    Expression Operand) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>Ternary <c>Condition ? Then : Else</c>.</summary>
/// <param name="Condition">The condition.</param>
/// <param name="Then">The value when the condition is truthy.</param>
/// <param name="Else">The value when the condition is falsy.</param>
public sealed record ConditionalExpression(
    Expression Condition,
    Expression Then,
    Expression Else) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>Author-written grouping <c>( Inner )</c>, retained to preserve intent.</summary>
/// <param name="Inner">The grouped expression.</param>
public sealed record ParenthesizedExpression(Expression Inner) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>Target[Index]</c>.</summary>
/// <param name="Target">The indexed expression.</param>
/// <param name="Index">The index expression.</param>
public sealed record IndexExpression(Expression Target, Expression Index) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>Target.Member</c>. <see cref="Member"/> is kept as a <c>string</c> so the parser accepts any
/// <c>.ident</c>. Member validity is a runtime concern in OpenSCAD — vectors expose <c>.x/.y/.z</c>,
/// ranges <c>.begin/.step/.end</c>, and objects (<c>textmetrics</c>/<c>fontmetrics</c>) arbitrary
/// members; an unmatched member yields <c>undef</c>, never a compile-time error — so it is not
/// statically validated.
/// </summary>
/// <param name="Target">The accessed expression.</param>
/// <param name="Member">The member name.</param>
public sealed record MemberExpression(Expression Target, string Member) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>Callee(Arguments)</c>. <see cref="Callee"/> is usually an <see cref="Identifier"/> but may be
/// any expression to support immediately-invoked function literals.
/// </summary>
/// <param name="Callee">The expression being called.</param>
/// <param name="Arguments">The call arguments.</param>
public sealed record FunctionCallExpression(
    Expression Callee,
    IReadOnlyList<Argument> Arguments) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>let (Bindings) Body</c> — expression form. (Parsed in Slice 3.)</summary>
/// <param name="Bindings">The let bindings.</param>
/// <param name="Body">The body expression.</param>
public sealed record LetExpression(
    IReadOnlyList<Binding> Bindings,
    Expression Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>assert(Arguments) Body</c> — <see cref="Body"/> is the expression the assert guards and is
/// optional (OpenSCAD grammar <c>expr_or_empty</c>). (Parsed in Slice 3.)
/// </summary>
/// <param name="Arguments">The assert arguments (condition, optional message).</param>
/// <param name="Body">The guarded expression, or <c>null</c>.</param>
public sealed record AssertExpression(
    IReadOnlyList<Argument> Arguments,
    Expression? Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>echo(Arguments) Body</c> — echoes then evaluates <see cref="Body"/>, which is optional
/// (OpenSCAD grammar <c>expr_or_empty</c>). (Parsed in Slice 3.)
/// </summary>
/// <param name="Arguments">The echo arguments.</param>
/// <param name="Body">The evaluated expression, or <c>null</c>.</param>
public sealed record EchoExpression(
    IReadOnlyList<Argument> Arguments,
    Expression? Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>Anonymous function literal: <c>function (Parameters) Body</c>. (Parsed in Slice 3.)</summary>
/// <param name="Parameters">The formal parameters.</param>
/// <param name="Body">The body expression.</param>
public sealed record FunctionLiteral(
    IReadOnlyList<Parameter> Parameters,
    Expression Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>for (Bindings) Body</c> inside <c>[...]</c>. Multiple bindings = cartesian product.
/// Valid only as a vector element. (Parsed in Slice 3.)
/// </summary>
/// <param name="Bindings">The comprehension bindings.</param>
/// <param name="Body">The yielded body.</param>
public sealed record ForComprehension(
    IReadOnlyList<Binding> Bindings,
    Expression Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// C-style <c>for (Init; Condition; Update) Body</c> inside <c>[...]</c> (OpenSCAD grammar
/// <c>LcForC</c>). Valid only as a vector element. (Parsed in Slice 3.)
/// </summary>
/// <param name="Init">The initial bindings.</param>
/// <param name="Condition">The loop test.</param>
/// <param name="Update">The update bindings.</param>
/// <param name="Body">The yielded body.</param>
public sealed record ForCComprehension(
    IReadOnlyList<Binding> Init,
    Expression Condition,
    IReadOnlyList<Binding> Update,
    Expression Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>if (Condition) Then</c> inside <c>[...]</c>, optionally <c>else Else</c>. Without an else it
/// acts as a filter; with one it selects between two yields. (Parsed in Slice 3.)
/// </summary>
/// <param name="Condition">The condition.</param>
/// <param name="Then">The value yielded when truthy.</param>
/// <param name="Else">The value yielded when falsy, or <c>null</c> (filter form).</param>
public sealed record IfComprehension(
    Expression Condition,
    Expression Then,
    Expression? Else) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>let (Bindings) Body</c> inside <c>[...]</c>. (Parsed in Slice 3.)</summary>
/// <param name="Bindings">The comprehension bindings.</param>
/// <param name="Body">The yielded body.</param>
public sealed record LetComprehension(
    IReadOnlyList<Binding> Bindings,
    Expression Body) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>each Value</c> — flattens <see cref="Value"/> into the surrounding vector. (Parsed in Slice 3.)</summary>
/// <param name="Value">The list to flatten.</param>
public sealed record EachExpression(Expression Value) : Expression
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}
