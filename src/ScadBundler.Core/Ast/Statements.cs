namespace ScadBundler.Core.Ast;

/// <summary>Base of all statement nodes.</summary>
public abstract record Statement : AstNode;

/// <summary>
/// <c>include &lt;path&gt;</c> — pulls in all definitions AND executes the file's top-level
/// statements at this point.
/// </summary>
/// <param name="RawPath">The text between <c>&lt;</c> and <c>&gt;</c> (no angle brackets).</param>
public sealed record IncludeStatement(string RawPath) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>use &lt;path&gt;</c> — imports only module and function definitions from the file; does not
/// execute its top-level statements and does not propagate that file's own include/use.
/// </summary>
/// <param name="RawPath">The text between <c>&lt;</c> and <c>&gt;</c> (no angle brackets).</param>
public sealed record UseStatement(string RawPath) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>module Name(Parameters) Body</c>. The body is typically a <see cref="BlockStatement"/> but
/// the grammar permits any single statement.
/// </summary>
/// <param name="Name">The module name.</param>
/// <param name="Parameters">The formal parameters.</param>
/// <param name="Body">The module body statement.</param>
public sealed record ModuleDefinition(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    Statement Body) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>function Name(Parameters) = Body;</c>.
/// </summary>
/// <param name="Name">The function name.</param>
/// <param name="Parameters">The formal parameters.</param>
/// <param name="Body">The function body expression.</param>
public sealed record FunctionDefinition(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    Expression Body) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>Name = Value;</c> at file scope or inside a block. At file scope and before the first
/// definition, these double as Customizer parameters.
/// </summary>
/// <param name="Name">The assigned name.</param>
/// <param name="Value">The assigned value expression.</param>
public sealed record AssignmentStatement(string Name, Expression Value) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// A call to a module: <c>Modifiers Name(Arguments) Child</c>. Covers built-ins, user modules, and
/// the statement forms of <c>echo</c>/<c>assert</c>/<c>children</c>/<c>assign</c>.
/// <see cref="Child"/> encodes what follows the <c>)</c>: <c>null</c> when terminated by <c>;</c>,
/// a single <see cref="ModuleInstantiation"/> for a chained child, or a <see cref="BlockStatement"/>
/// for braced children.
/// </summary>
/// <param name="Modifiers">The geometry modifiers, outer→inner as written.</param>
/// <param name="Name">The module name being instantiated.</param>
/// <param name="Arguments">The call arguments.</param>
/// <param name="Child">The child statement, or <c>null</c> when terminated by <c>;</c>.</param>
public sealed record ModuleInstantiation(
    IReadOnlyList<InstantiationModifier> Modifiers,
    string Name,
    IReadOnlyList<Argument> Arguments,
    Statement? Child) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>{ Statements }</c>.</summary>
/// <param name="Statements">The statements inside the block.</param>
public sealed record BlockStatement(IReadOnlyList<Statement> Statements) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>if (Condition) Then</c> with optional <c>else Else</c>. <see cref="Else"/> may itself be an
/// <see cref="IfStatement"/> to represent <c>else if</c>.
/// </summary>
/// <param name="Condition">The branch condition.</param>
/// <param name="Then">The then-branch statement.</param>
/// <param name="Else">The else-branch statement, or <c>null</c>.</param>
public sealed record IfStatement(
    Expression Condition,
    Statement Then,
    Statement? Else) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// <c>for (Bindings) Body</c> — bindings iterate as a cartesian product when multiple.
/// </summary>
/// <param name="Bindings">The loop bindings.</param>
/// <param name="Body">The loop body statement.</param>
public sealed record ForStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>intersection_for (Bindings) Body</c>.</summary>
/// <param name="Bindings">The loop bindings.</param>
/// <param name="Body">The loop body statement.</param>
public sealed record IntersectionForStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary><c>let (Bindings) Body</c> — statement form (geometry scope).</summary>
/// <param name="Bindings">The let bindings.</param>
/// <param name="Body">The body statement.</param>
public sealed record LetStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body) : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>A lone <c>;</c>. Retained for fidelity; the emitter may elide it.</summary>
public sealed record EmptyStatement() : Statement
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}
