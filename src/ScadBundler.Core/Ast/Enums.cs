namespace ScadBundler.Core.Ast;

/// <summary>
/// A geometry modifier that can prefix a module instantiation (<c>* ! # %</c>). Modifiers may
/// stack (<c>#%cube();</c>) and are stored outer→inner as written.
/// </summary>
public enum InstantiationModifier
{
    /// <summary><c>*</c> — disable: treat the subtree as if commented out.</summary>
    Disable,

    /// <summary><c>!</c> — root: render only this subtree.</summary>
    Root,

    /// <summary><c>#</c> — highlight: render highlighted (debug).</summary>
    Highlight,

    /// <summary><c>%</c> — background: render transparent, excluded from geometry.</summary>
    Background,
}

/// <summary>A prefix unary operator.</summary>
public enum UnaryOperator
{
    /// <summary><c>-</c> arithmetic negation.</summary>
    Negate,

    /// <summary><c>+</c> identity (kept for round-trip fidelity; no runtime effect).</summary>
    Plus,

    /// <summary><c>!</c> logical not.</summary>
    Not,

    /// <summary><c>~</c> bitwise not.</summary>
    BitwiseNot,
}

/// <summary>A binary operator. Precedence/associativity are defined in <c>docs/Parser-Planning.md</c>.</summary>
public enum BinaryOperator
{
    /// <summary><c>+</c></summary>
    Add,

    /// <summary><c>-</c></summary>
    Subtract,

    /// <summary><c>*</c></summary>
    Multiply,

    /// <summary><c>/</c></summary>
    Divide,

    /// <summary><c>%</c></summary>
    Modulo,

    /// <summary><c>^</c> (right-associative; binds tighter than unary minus).</summary>
    Power,

    /// <summary><c>&lt;</c></summary>
    Less,

    /// <summary><c>&lt;=</c></summary>
    LessEqual,

    /// <summary><c>&gt;</c></summary>
    Greater,

    /// <summary><c>&gt;=</c></summary>
    GreaterEqual,

    /// <summary><c>==</c></summary>
    Equal,

    /// <summary><c>!=</c></summary>
    NotEqual,

    /// <summary><c>&amp;&amp;</c></summary>
    And,

    /// <summary><c>||</c></summary>
    Or,

    /// <summary><c>&amp;</c></summary>
    BitwiseAnd,

    /// <summary><c>|</c></summary>
    BitwiseOr,

    /// <summary><c>&lt;&lt;</c></summary>
    ShiftLeft,

    /// <summary><c>&gt;&gt;</c></summary>
    ShiftRight,
}
