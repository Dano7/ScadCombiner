using ScadBundler.Core.Lexing;

namespace ScadBundler.Core.Parsing;

/// <summary>
/// A forward cursor over the lexer's token stream. Provides the position primitives the parser
/// builds on: <see cref="Current"/>, <see cref="Peek"/>, <see cref="Advance"/>, <see cref="Check"/>,
/// and <see cref="Match"/>. The stream always ends with a single <see cref="TokenKind.Eof"/> token;
/// advancing past it is clamped so the cursor never runs off the end.
/// </summary>
internal sealed class TokenCursor
{
    private readonly IReadOnlyList<Token> _tokens;

    /// <summary>Creates a cursor over a token stream (must end with exactly one EOF token).</summary>
    /// <param name="tokens">The token stream to navigate.</param>
    public TokenCursor(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
        Previous = tokens[0];
    }

    /// <summary>The current token.</summary>
    public Token Current => _tokens[Position];

    /// <summary>The kind of the current token.</summary>
    public TokenKind Kind => Current.Kind;

    /// <summary>The most recently consumed token (the first token before any advance).</summary>
    public Token Previous { get; private set; }

    /// <summary>The current index into the token stream (used to detect lack of progress).</summary>
    public int Position { get; private set; }

    /// <summary>True when the current token is the terminating EOF.</summary>
    public bool AtEnd => Kind == TokenKind.Eof;

    /// <summary>Returns the token <paramref name="ahead"/> positions from the current one (clamped to EOF).</summary>
    /// <param name="ahead">How many tokens ahead to look (0 = current).</param>
    /// <returns>The looked-ahead token, or the EOF token if past the end.</returns>
    public Token Peek(int ahead = 1)
    {
        int i = Position + ahead;
        return i < _tokens.Count ? _tokens[i] : _tokens[^1];
    }

    /// <summary>Consumes and returns the current token, advancing the cursor (clamped at EOF).</summary>
    /// <returns>The token that was current before advancing.</returns>
    public Token Advance()
    {
        Token current = Current;
        Previous = current;
        if (Position < _tokens.Count - 1)
        {
            Position++;
        }

        return current;
    }

    /// <summary>True when the current token is of the given kind.</summary>
    /// <param name="kind">The kind to test.</param>
    /// <returns><c>true</c> if the current token matches.</returns>
    public bool Check(TokenKind kind) => Kind == kind;

    /// <summary>Consumes the current token if it is of the given kind.</summary>
    /// <param name="kind">The kind to match.</param>
    /// <returns><c>true</c> and advances if matched; otherwise <c>false</c>.</returns>
    public bool Match(TokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }

        return false;
    }
}
