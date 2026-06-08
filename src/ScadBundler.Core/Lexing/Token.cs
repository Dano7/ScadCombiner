using ScadBundler.Core.Text;

namespace ScadBundler.Core.Lexing;

/// <summary>
/// A single lexical token with its precise source span, attached comment trivia, and (for
/// numbers/strings) its decoded value. Implemented as a <c>readonly record struct</c> for low
/// allocation; the trivia lists default to empty.
/// </summary>
public readonly record struct Token
{
    /// <summary>Creates a token with empty trivia defaults; required members are set via initializer.</summary>
    public Token()
    {
    }

    /// <summary>The kind of token.</summary>
    public required TokenKind Kind { get; init; }

    /// <summary>
    /// Raw source lexeme. For <see cref="TokenKind.Number"/>/<see cref="TokenKind.String"/> this is
    /// the verbatim text (the AST's <c>RawText</c>); for <see cref="TokenKind.FilePath"/> it is the
    /// raw text between <c>&lt;</c> and <c>&gt;</c> (no delimiters).
    /// </summary>
    public required string Text { get; init; }

    /// <summary>The half-open source span of the lexeme (excludes trivia).</summary>
    public required SourceSpan Span { get; init; }

    /// <summary>Comments attached before this token, in source order. Empty when none.</summary>
    public IReadOnlyList<Trivia> LeadingTrivia { get; init; } = [];

    /// <summary>Comments on the same line after this token (e.g. a Customizer annotation). Empty when none.</summary>
    public IReadOnlyList<Trivia> TrailingTrivia { get; init; } = [];

    /// <summary>True when one or more blank lines preceded this token.</summary>
    public bool BlankLineBefore { get; init; }

    /// <summary>Decoded value for <see cref="TokenKind.Number"/> (parsed double, incl. hex). Null otherwise.</summary>
    public double? NumberValue { get; init; }

    /// <summary>
    /// Decoded value for <see cref="TokenKind.String"/> (escapes resolved). For
    /// <see cref="TokenKind.FilePath"/>, the raw path. Null otherwise.
    /// </summary>
    public string? StringValue { get; init; }
}
