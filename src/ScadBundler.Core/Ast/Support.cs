namespace ScadBundler.Core.Ast;

/// <summary>
/// A formal parameter in a module, function, or function-literal definition.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="DefaultValue">The default value, or <c>null</c> when the parameter has none.</param>
public sealed record Parameter(string Name, Expression? DefaultValue) : AstNode
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// An argument in a call. <see cref="Name"/> is <c>null</c> for positional arguments and set for
/// named arguments (<c>cube(size = 5)</c>).
/// </summary>
/// <param name="Name">The argument name for named arguments; <c>null</c> when positional.</param>
/// <param name="Value">The argument value expression.</param>
public sealed record Argument(string? Name, Expression Value) : AstNode
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>
/// A <c>Name = Value</c> binding used by <c>let</c>/<c>for</c> and their comprehension forms.
/// Distinct from <see cref="AssignmentStatement"/>, which occupies statement position.
/// </summary>
/// <param name="Name">The bound name.</param>
/// <param name="Value">The bound value expression.</param>
public sealed record Binding(string Name, Expression Value) : AstNode
{
    /// <inheritdoc/>
    public override TResult Accept<TResult>(IAstVisitor<TResult> visitor) => visitor.Visit(this);
}
