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
}
