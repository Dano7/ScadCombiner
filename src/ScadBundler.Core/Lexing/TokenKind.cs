namespace ScadBundler.Core.Lexing;

/// <summary>
/// The complete set of lexical token kinds produced by the <see cref="Lexer"/>.
/// </summary>
/// <remarks>
/// The statement modifiers <c>* ! # %</c> are lexed as <see cref="Star"/>, <see cref="Not"/>,
/// <see cref="Hash"/>, and <see cref="Percent"/> — the parser decides modifier-vs-operator by
/// position. The keyword set is exactly {module, function, if, else, for, let, assert, echo,
/// each, true, false, undef} plus contextual <c>include</c>/<c>use</c>. Names such as
/// <c>intersection_for</c>, <c>cube</c>, and <c>translate</c> are <see cref="Identifier"/>s.
/// </remarks>
public enum TokenKind
{
    // literals & identifiers

    /// <summary>An identifier (includes special variables like <c>$fn</c> and built-in names).</summary>
    Identifier,

    /// <summary>A numeric literal (decimal, fraction, scientific, or hex).</summary>
    Number,

    /// <summary>A string literal.</summary>
    String,

    /// <summary>The keyword <c>true</c>.</summary>
    True,

    /// <summary>The keyword <c>false</c>.</summary>
    False,

    /// <summary>The keyword <c>undef</c>.</summary>
    Undef,

    // keywords (contextual ones noted)

    /// <summary>The keyword <c>module</c>.</summary>
    Module,

    /// <summary>The keyword <c>function</c>.</summary>
    Function,

    /// <summary>The keyword <c>if</c>.</summary>
    If,

    /// <summary>The keyword <c>else</c>.</summary>
    Else,

    /// <summary>The keyword <c>for</c>.</summary>
    For,

    /// <summary>The keyword <c>let</c>.</summary>
    Let,

    /// <summary>The keyword <c>assert</c>.</summary>
    Assert,

    /// <summary>The keyword <c>echo</c>.</summary>
    Echo,

    /// <summary>The keyword <c>each</c>.</summary>
    Each,

    /// <summary>The keyword <c>include</c> (only when followed by <c>&lt;</c>).</summary>
    Include,

    /// <summary>The keyword <c>use</c> (only when followed by <c>&lt;</c>).</summary>
    Use,

    /// <summary>The <c>&lt;...&gt;</c> path text following <see cref="Include"/>/<see cref="Use"/>.</summary>
    FilePath,

    // assignment & punctuation

    /// <summary><c>=</c></summary>
    Assign,

    /// <summary><c>(</c></summary>
    LParen,

    /// <summary><c>)</c></summary>
    RParen,

    /// <summary><c>{</c></summary>
    LBrace,

    /// <summary><c>}</c></summary>
    RBrace,

    /// <summary><c>[</c></summary>
    LBracket,

    /// <summary><c>]</c></summary>
    RBracket,

    /// <summary><c>;</c></summary>
    Semicolon,

    /// <summary><c>,</c></summary>
    Comma,

    /// <summary><c>:</c></summary>
    Colon,

    /// <summary><c>.</c></summary>
    Dot,

    /// <summary><c>?</c></summary>
    Question,

    // operators

    /// <summary><c>+</c></summary>
    Plus,

    /// <summary><c>-</c></summary>
    Minus,

    /// <summary><c>*</c></summary>
    Star,

    /// <summary><c>/</c></summary>
    Slash,

    /// <summary><c>%</c></summary>
    Percent,

    /// <summary><c>^</c></summary>
    Caret,

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

    /// <summary><c>!</c></summary>
    Not,

    /// <summary><c>&amp;</c></summary>
    Amp,

    /// <summary><c>|</c></summary>
    Pipe,

    /// <summary><c>~</c></summary>
    Tilde,

    /// <summary><c>&lt;&lt;</c></summary>
    ShiftLeft,

    /// <summary><c>&gt;&gt;</c></summary>
    ShiftRight,

    /// <summary><c>#</c> (highlight modifier).</summary>
    Hash,

    /// <summary>End of input. The token stream always ends with exactly one of these.</summary>
    Eof,
}
