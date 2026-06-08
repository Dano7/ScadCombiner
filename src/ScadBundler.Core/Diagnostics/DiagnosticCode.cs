namespace ScadBundler.Core.Diagnostics;

/// <summary>
/// The canonical <c>SBnnnn</c> diagnostic codes. The first digit groups by pipeline phase
/// (1 = lexing, 2 = parsing, …). See <c>docs/Diagnostics.md</c> for the authoritative catalog.
/// Codes must never be invented at implementation time without being recorded there first.
/// </summary>
public static class DiagnosticCode
{
    // Lexer (SB1xxx)

    /// <summary>Unterminated string literal.</summary>
    public const string UnterminatedString = "SB1001";

    /// <summary>Unterminated block comment.</summary>
    public const string UnterminatedBlockComment = "SB1002";

    /// <summary>Unterminated <c>include</c>/<c>use</c> statement (<c>&lt;</c> with no <c>&gt;</c>).</summary>
    public const string UnterminatedIncludeUse = "SB1003";

    /// <summary>Unexpected/unrecognized character.</summary>
    public const string UnexpectedCharacter = "SB1004";

    /// <summary>Non-ASCII character outside a string or comment.</summary>
    public const string NonAsciiCharacter = "SB1005";

    /// <summary>Undefined string escape sequence.</summary>
    public const string UndefinedEscape = "SB1006";

    /// <summary>Integer/hex literal too large to be represented precisely as a double.</summary>
    public const string ImpreciseNumber = "SB1007";

    /// <summary>Identifier starting with a digit (deprecated).</summary>
    public const string DigitLeadingIdentifier = "SB1008";

    /// <summary>Newline inside an <c>include</c>/<c>use</c> path.</summary>
    public const string NewlineInIncludePath = "SB1009";

    // Parser (SB2xxx)

    /// <summary>A specific expected token is missing.</summary>
    public const string ExpectedToken = "SB2001";

    /// <summary>A token is valid nowhere here, or input ended unexpectedly.</summary>
    public const string UnexpectedToken = "SB2002";

    /// <summary>An opening <c>(</c>/<c>[</c>/<c>{</c> has no matching close.</summary>
    public const string UnclosedDelimiter = "SB2003";

    /// <summary>A statement or definition is not terminated by <c>;</c>.</summary>
    public const string MissingSemicolon = "SB2004";

    /// <summary>An expression was expected but none was found.</summary>
    public const string ExpectedExpression = "SB2005";

    /// <summary>A parameter list is malformed.</summary>
    public const string InvalidParameterList = "SB2006";

    /// <summary>An argument list is malformed.</summary>
    public const string InvalidArgumentList = "SB2007";

    // Semantic analysis (SB3xxx)

    /// <summary>A vector member access names a component outside <c>{x, y, z}</c>.</summary>
    public const string InvalidMemberAccess = "SB3001";

    /// <summary>A list-comprehension generator appears outside a <see cref="Ast.VectorExpression"/>.</summary>
    public const string ComprehensionOutsideVector = "SB3002";

    /// <summary>A variable is reassigned within a scope; the last assignment wins.</summary>
    public const string VariableReassigned = "SB3003";

    /// <summary>A module or function is redefined within a scope; the last definition wins.</summary>
    public const string DefinitionRedefined = "SB3004";

    /// <summary>
    /// A reference resolves to nothing — not a built-in, special variable, local binding, or any
    /// reachable user declaration. Emitted conservatively (only when all files are loaded).
    /// </summary>
    public const string UnknownReference = "SB3005";
}
