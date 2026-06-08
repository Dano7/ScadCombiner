using System.Globalization;
using System.Text;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Lexing;

/// <summary>
/// A hand-written scanner that turns OpenSCAD source text into a token stream with precise source
/// spans, attached comment trivia, and collected diagnostics. The lexer never throws on malformed
/// input — every error is reported via <see cref="LexResult.Diagnostics"/> and recovered from.
/// </summary>
/// <remarks>
/// Lexical behaviour mirrors OpenSCAD's <c>src/core/lexer.l</c> (openscad-2019.05-3933).
/// </remarks>
public sealed class Lexer
{
    private readonly SourceFile _source;
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly List<Token> _tokens = [];

    private int _index;
    private int _line = 1;
    private int _column = 1;

    private Lexer(SourceFile source)
    {
        _source = source;
        _text = source.Text;
    }

    /// <summary>
    /// Tokenizes the whole file. The returned token list always ends with a single EOF token.
    /// Never throws on malformed input — errors are reported via <see cref="LexResult.Diagnostics"/>.
    /// </summary>
    /// <param name="source">The source file to tokenize.</param>
    /// <returns>The token stream and any diagnostics.</returns>
    public static LexResult Lex(SourceFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Lexer(source).LexAll();
    }

    private LexResult LexAll()
    {
        while (true)
        {
            (List<Trivia> leading, bool blankBefore) = ScanLeadingTrivia();

            if (AtEnd)
            {
                SourcePosition eof = CurrentPosition();
                _tokens.Add(new Token
                {
                    Kind = TokenKind.Eof,
                    Text = string.Empty,
                    Span = new SourceSpan(_source, eof, eof),
                    LeadingTrivia = leading,
                    BlankLineBefore = blankBefore,
                });
                break;
            }

            int firstIndex = _tokens.Count;
            ScanConstruct();

            Token first = _tokens[firstIndex];
            _tokens[firstIndex] = first with { LeadingTrivia = leading, BlankLineBefore = blankBefore };

            List<Trivia> trailing = ScanTrailingTrivia();
            if (trailing.Count > 0)
            {
                int lastIndex = _tokens.Count - 1;
                _tokens[lastIndex] = _tokens[lastIndex] with { TrailingTrivia = trailing };
            }
        }

        return new LexResult(_tokens, _diagnostics.ToList());
    }

    // ---------------------------------------------------------------------------------------------
    // Trivia & whitespace
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Consumes whitespace, comments, and invalid characters up to the next real token start (or
    /// EOF), collecting comments as leading trivia and detecting whether a blank line was crossed.
    /// </summary>
    private (List<Trivia> Trivia, bool BlankLineBefore) ScanLeadingTrivia()
    {
        var trivia = new List<Trivia>();
        bool sawBlank = false;
        bool lineHadContent = true;

        while (!AtEnd)
        {
            char c = Peek();
            if (IsInlineWhitespace(c))
            {
                Advance();
                continue;
            }

            if (c == '\n')
            {
                if (!lineHadContent)
                {
                    sawBlank = true;
                }

                lineHadContent = false;
                Advance();
                continue;
            }

            if (c == '/' && Peek(1) == '/')
            {
                trivia.Add(ScanLineComment());
                lineHadContent = true;
                continue;
            }

            if (c == '/' && Peek(1) == '*')
            {
                trivia.Add(ScanBlockComment());
                lineHadContent = true;
                continue;
            }

            if (IsTokenStart(c))
            {
                break;
            }

            // Unrecognized character: report and skip one character (recovery).
            ReportInvalidCharacter(c);
            Advance();
            lineHadContent = true;
        }

        return (trivia, sawBlank);
    }

    /// <summary>
    /// Collects comments that appear on the same line as the just-emitted token, before the next
    /// newline, as trailing trivia (this is how Customizer inline annotations are preserved).
    /// </summary>
    private List<Trivia> ScanTrailingTrivia()
    {
        var trailing = new List<Trivia>();

        while (true)
        {
            int markIndex = _index;
            int markLine = _line;
            int markColumn = _column;

            while (!AtEnd && IsInlineWhitespace(Peek()))
            {
                Advance();
            }

            if (AtEnd)
            {
                Restore(markIndex, markLine, markColumn);
                break;
            }

            char c = Peek();
            if (c == '/' && Peek(1) == '/')
            {
                trailing.Add(ScanLineComment());
                continue;
            }

            if (c == '/' && Peek(1) == '*')
            {
                int startLine = _line;
                trailing.Add(ScanBlockComment());
                if (_line != startLine)
                {
                    // The block comment spanned a newline; anything after belongs to the next token.
                    break;
                }

                continue;
            }

            // Not a same-line comment: the whitespace we skipped belongs to the next token.
            Restore(markIndex, markLine, markColumn);
            break;
        }

        return trailing;
    }

    private CommentTrivia ScanLineComment()
    {
        SourcePosition start = CurrentPosition();
        Advance(); // '/'
        Advance(); // '/'
        while (!AtEnd)
        {
            char c = Peek();
            if (c == '\n' || c == '\r')
            {
                break;
            }

            Advance();
        }

        SourcePosition end = CurrentPosition();
        string text = Slice(start, end);
        return new CommentTrivia(text, CommentKind.Line) { Span = new SourceSpan(_source, start, end) };
    }

    private CommentTrivia ScanBlockComment()
    {
        SourcePosition start = CurrentPosition();
        Advance(); // '/'
        Advance(); // '*'
        bool closed = false;
        while (!AtEnd)
        {
            if (Peek() == '*' && Peek(1) == '/')
            {
                Advance();
                Advance();
                closed = true;
                break;
            }

            Advance();
        }

        SourcePosition end = CurrentPosition();
        if (!closed)
        {
            _diagnostics.Error(
                DiagnosticCode.UnterminatedBlockComment,
                "Unterminated block comment.",
                new SourceSpan(_source, start, end));
        }

        string text = Slice(start, end);
        return new CommentTrivia(text, CommentKind.Block) { Span = new SourceSpan(_source, start, end) };
    }

    // ---------------------------------------------------------------------------------------------
    // Token dispatch
    // ---------------------------------------------------------------------------------------------

    private void ScanConstruct()
    {
        char c = Peek();

        if (IsDigit(c) || (c == '.' && IsDigit(Peek(1))))
        {
            ScanNumber();
            return;
        }

        if (IsIdentifierStart(c))
        {
            ScanIdentifierOrKeyword();
            return;
        }

        if (c == '"')
        {
            ScanString();
            return;
        }

        ScanOperatorOrPunctuation();
    }

    private void ScanOperatorOrPunctuation()
    {
        char c = Peek();
        char n = Peek(1);

        switch (c)
        {
            case '=': EmitFixed(n == '=' ? TokenKind.Equal : TokenKind.Assign, n == '=' ? 2 : 1); return;
            case '!': EmitFixed(n == '=' ? TokenKind.NotEqual : TokenKind.Not, n == '=' ? 2 : 1); return;
            case '<':
                EmitFixed(n == '=' ? TokenKind.LessEqual : n == '<' ? TokenKind.ShiftLeft : TokenKind.Less, n is '=' or '<' ? 2 : 1);
                return;
            case '>':
                EmitFixed(n == '=' ? TokenKind.GreaterEqual : n == '>' ? TokenKind.ShiftRight : TokenKind.Greater, n is '=' or '>' ? 2 : 1);
                return;
            case '&': EmitFixed(n == '&' ? TokenKind.And : TokenKind.Amp, n == '&' ? 2 : 1); return;
            case '|': EmitFixed(n == '|' ? TokenKind.Or : TokenKind.Pipe, n == '|' ? 2 : 1); return;
            case '+': EmitFixed(TokenKind.Plus, 1); return;
            case '-': EmitFixed(TokenKind.Minus, 1); return;
            case '*': EmitFixed(TokenKind.Star, 1); return;
            case '/': EmitFixed(TokenKind.Slash, 1); return;
            case '%': EmitFixed(TokenKind.Percent, 1); return;
            case '^': EmitFixed(TokenKind.Caret, 1); return;
            case '~': EmitFixed(TokenKind.Tilde, 1); return;
            case '#': EmitFixed(TokenKind.Hash, 1); return;
            case '(': EmitFixed(TokenKind.LParen, 1); return;
            case ')': EmitFixed(TokenKind.RParen, 1); return;
            case '[': EmitFixed(TokenKind.LBracket, 1); return;
            case ']': EmitFixed(TokenKind.RBracket, 1); return;
            case '{': EmitFixed(TokenKind.LBrace, 1); return;
            case '}': EmitFixed(TokenKind.RBrace, 1); return;
            case ';': EmitFixed(TokenKind.Semicolon, 1); return;
            case ',': EmitFixed(TokenKind.Comma, 1); return;
            case ':': EmitFixed(TokenKind.Colon, 1); return;
            case '.': EmitFixed(TokenKind.Dot, 1); return;
            case '?': EmitFixed(TokenKind.Question, 1); return;
            default:
                // Unreachable: ScanConstruct only delegates here for a known operator/punctuation start.
                throw new InvalidOperationException($"Unexpected token start '{c}'.");
        }
    }

    private void EmitFixed(TokenKind kind, int length)
    {
        SourcePosition start = CurrentPosition();
        for (int i = 0; i < length; i++)
        {
            Advance();
        }

        SourcePosition end = CurrentPosition();
        _tokens.Add(new Token
        {
            Kind = kind,
            Text = Slice(start, end),
            Span = new SourceSpan(_source, start, end),
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Identifiers, keywords, include/use
    // ---------------------------------------------------------------------------------------------

    private void ScanIdentifierOrKeyword()
    {
        SourcePosition start = CurrentPosition();
        Advance(); // identifier start char
        while (!AtEnd && IsIdentifierRest(Peek()))
        {
            Advance();
        }

        SourcePosition end = CurrentPosition();
        string text = Slice(start, end);

        if (text is "include" or "use")
        {
            // Contextual: keyword only when the next non-whitespace character is '<'.
            int saveIndex = _index;
            int saveLine = _line;
            int saveColumn = _column;

            while (!AtEnd && IsIncludeGapWhitespace(Peek()))
            {
                Advance();
            }

            if (!AtEnd && Peek() == '<')
            {
                _tokens.Add(new Token
                {
                    Kind = text == "include" ? TokenKind.Include : TokenKind.Use,
                    Text = text,
                    Span = new SourceSpan(_source, start, end),
                });
                Advance(); // consume '<'
                ScanFilePath();
                return;
            }

            // Not an include/use directive; rewind the lookahead and treat as an identifier.
            Restore(saveIndex, saveLine, saveColumn);
        }

        _tokens.Add(new Token
        {
            Kind = KeywordKind(text),
            Text = text,
            Span = new SourceSpan(_source, start, end),
        });
    }

    private void ScanFilePath()
    {
        SourcePosition start = CurrentPosition(); // first char after '<'
        var path = new StringBuilder();
        bool warnedNewline = false;

        while (true)
        {
            if (AtEnd)
            {
                SourcePosition eofEnd = CurrentPosition();
                _diagnostics.Error(
                    DiagnosticCode.UnterminatedIncludeUse,
                    "Unterminated include/use statement.",
                    new SourceSpan(_source, start, eofEnd));
                EmitFilePath(start, eofEnd, path.ToString());
                return;
            }

            char c = Peek();
            if (c == '>')
            {
                SourcePosition end = CurrentPosition();
                EmitFilePath(start, end, path.ToString());
                Advance(); // consume '>'
                return;
            }

            if (c == '\n')
            {
                if (!warnedNewline)
                {
                    SourcePosition at = CurrentPosition();
                    _diagnostics.Warning(
                        DiagnosticCode.NewlineInIncludePath,
                        "Newline in include/use path is not well-defined.",
                        new SourceSpan(_source, at, at));
                    warnedNewline = true;
                }

                Advance(); // newlines are not part of the path
                continue;
            }

            if (c == '\r')
            {
                Advance();
                continue;
            }

            path.Append(c);
            Advance();
        }
    }

    private void EmitFilePath(SourcePosition start, SourcePosition end, string path)
    {
        _tokens.Add(new Token
        {
            Kind = TokenKind.FilePath,
            Text = path,
            Span = new SourceSpan(_source, start, end),
            StringValue = path,
        });
    }

    private static TokenKind KeywordKind(string text) => text switch
    {
        "module" => TokenKind.Module,
        "function" => TokenKind.Function,
        "if" => TokenKind.If,
        "else" => TokenKind.Else,
        "for" => TokenKind.For,
        "let" => TokenKind.Let,
        "assert" => TokenKind.Assert,
        "echo" => TokenKind.Echo,
        "each" => TokenKind.Each,
        "true" => TokenKind.True,
        "false" => TokenKind.False,
        "undef" => TokenKind.Undef,
        _ => TokenKind.Identifier,
    };

    // ---------------------------------------------------------------------------------------------
    // Strings
    // ---------------------------------------------------------------------------------------------

    private void ScanString()
    {
        SourcePosition start = CurrentPosition(); // opening quote
        Advance();
        var value = new StringBuilder();
        bool terminated = false;

        while (!AtEnd)
        {
            char c = Peek();
            if (c == '"')
            {
                Advance();
                terminated = true;
                break;
            }

            if (c == '\n' || c == '\r')
            {
                // A string is bounded by the end of line; an unterminated one recovers here.
                break;
            }

            if (c == '\\')
            {
                ScanStringEscape(value);
                continue;
            }

            value.Append(c);
            Advance();
        }

        SourcePosition end = CurrentPosition();
        if (!terminated)
        {
            _diagnostics.Error(
                DiagnosticCode.UnterminatedString,
                "Unterminated string literal.",
                new SourceSpan(_source, start, end));
        }

        _tokens.Add(new Token
        {
            Kind = TokenKind.String,
            Text = Slice(start, end),
            Span = new SourceSpan(_source, start, end),
            StringValue = value.ToString(),
        });
    }

    private void ScanStringEscape(StringBuilder value)
    {
        SourcePosition escStart = CurrentPosition();
        Advance(); // backslash
        if (AtEnd)
        {
            // Trailing backslash at EOF; the surrounding loop reports the unterminated string.
            return;
        }

        char e = Peek();
        switch (e)
        {
            case 'n': value.Append('\n'); Advance(); return;
            case 't': value.Append('\t'); Advance(); return;
            case 'r': value.Append('\r'); Advance(); return;
            case '\\': value.Append('\\'); Advance(); return;
            case '"': value.Append('"'); Advance(); return;
            case 'x' when IsOctalDigit(Peek(1)) && IsHexDigit(Peek(2)):
            {
                int b = (HexValue(Peek(1)) << 4) | HexValue(Peek(2));
                // OpenSCAD maps \x00 to a space rather than a NUL byte.
                value.Append(b == 0 ? ' ' : (char)b);
                Advance(); // 'x'
                Advance(); // high nibble
                Advance(); // low nibble
                return;
            }

            case 'u' when TryReadHex(1, 4, out int cp4):
                AppendCodePoint(value, cp4);
                Advance(); // 'u'
                AdvanceMany(4);
                return;

            case 'U' when TryReadHex(1, 6, out int cp6):
                AppendCodePoint(value, cp6);
                Advance(); // 'U'
                AdvanceMany(6);
                return;

            default:
            {
                // Undefined escape: drop the backslash, keep the following character.
                var escEnd = new SourcePosition(escStart.Offset + 2, escStart.Line, escStart.Column + 2);
                _diagnostics.Warning(
                    DiagnosticCode.UndefinedEscape,
                    $"Undefined escape sequence '\\{e}'; backslash ignored.",
                    new SourceSpan(_source, escStart, escEnd));
                value.Append(e);
                Advance();
                return;
            }
        }
    }

    private static void AppendCodePoint(StringBuilder value, int codePoint)
    {
        bool isSurrogate = codePoint is >= 0xD800 and <= 0xDFFF;
        if (codePoint is >= 0 and <= 0x10FFFF && !isSurrogate)
        {
            value.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            value.Append('\uFFFD');
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Numbers
    // ---------------------------------------------------------------------------------------------

    private void ScanNumber()
    {
        SourcePosition start = CurrentPosition();

        int hexLength = HexNumberLength();
        int decimalLength = DecimalNumberLength();
        int identifierLength = DigitLeadingIdentifierLength();

        int numberLength = Math.Max(hexLength, decimalLength);
        bool isHex = hexLength > 0 && hexLength >= decimalLength;

        // Maximal munch: a digit-leading identifier (e.g. "2d") wins over a shorter number.
        if (identifierLength > numberLength)
        {
            AdvanceMany(identifierLength);
            SourcePosition idEnd = CurrentPosition();
            string idText = Slice(start, idEnd);
            _diagnostics.Warning(
                DiagnosticCode.DigitLeadingIdentifier,
                $"Variable names starting with a digit ('{idText}') are deprecated.",
                new SourceSpan(_source, start, idEnd));
            _tokens.Add(new Token
            {
                Kind = TokenKind.Identifier,
                Text = idText,
                Span = new SourceSpan(_source, start, idEnd),
            });
            return;
        }

        AdvanceMany(numberLength);
        SourcePosition end = CurrentPosition();
        string text = Slice(start, end);
        double value = isHex ? ParseHexValue(text, start, end) : ParseDecimalValue(text, start, end);

        _tokens.Add(new Token
        {
            Kind = TokenKind.Number,
            Text = text,
            Span = new SourceSpan(_source, start, end),
            NumberValue = value,
        });
    }

    private int HexNumberLength()
    {
        if (Peek(0) != '0' || Peek(1) != 'x' || !IsHexDigit(Peek(2)))
        {
            return 0;
        }

        int length = 2;
        while (IsHexDigit(Peek(length)))
        {
            length++;
        }

        return length;
    }

    private int DecimalNumberLength()
    {
        int i = 0;
        int intDigits = 0;
        while (IsDigit(Peek(i)))
        {
            i++;
            intDigits++;
        }

        bool hasDot = false;
        if (Peek(i) == '.')
        {
            int j = i + 1;
            bool anyFraction = false;
            while (IsDigit(Peek(j)))
            {
                j++;
                anyFraction = true;
            }

            // The dot joins the number only if there is a digit on at least one side.
            if (intDigits >= 1 || anyFraction)
            {
                hasDot = true;
                i = j;
            }
        }

        if (intDigits == 0 && !hasDot)
        {
            return 0;
        }

        if (Peek(i) is 'e' or 'E')
        {
            int j = i + 1;
            if (Peek(j) is '+' or '-')
            {
                j++;
            }

            if (IsDigit(Peek(j)))
            {
                while (IsDigit(Peek(j)))
                {
                    j++;
                }

                i = j;
            }
        }

        return i;
    }

    private int DigitLeadingIdentifierLength()
    {
        if (!IsDigit(Peek(0)))
        {
            return 0;
        }

        int length = 1;
        while (IsIdentifierRest(Peek(length)))
        {
            length++;
        }

        return length;
    }

    private double ParseHexValue(string text, SourcePosition start, SourcePosition end)
    {
        string digits = text[2..];
        if (ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong exact))
        {
            if (!IsExactlyRepresentable(exact))
            {
                WarnImprecise(text, start, end);
            }

            return exact;
        }

        // More than 64 bits of hex digits: best-effort double and a precision warning.
        WarnImprecise(text, start, end);
        double value = 0;
        foreach (char c in digits)
        {
            value = (value * 16) + HexValue(c);
        }

        return value;
    }

    private double ParseDecimalValue(string text, SourcePosition start, SourcePosition end)
    {
        bool isInteger = !text.Contains('.', StringComparison.Ordinal)
            && !text.Contains('e', StringComparison.Ordinal)
            && !text.Contains('E', StringComparison.Ordinal);

        if (isInteger)
        {
            if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong exact))
            {
                if (!IsExactlyRepresentable(exact))
                {
                    WarnImprecise(text, start, end);
                }

                return exact;
            }

            // Too large for 64 bits: warn, then fall back to the best-effort double below.
            WarnImprecise(text, start, end);
        }

        // Forms like "1." and "1.e10" are valid OpenSCAD but rejected by some parsers; normalize a
        // copy (the raw lexeme is preserved in Token.Text) and never throw.
        string normalized = NormalizeNumberForParse(text);
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.NaN;
    }

    private static string NormalizeNumberForParse(string text)
    {
        var sb = new StringBuilder(text.Length + 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '.')
            {
                if (sb.Length == 0 || !IsDigit(sb[^1]))
                {
                    sb.Append('0'); // ensure a digit precedes the decimal point
                }

                sb.Append('.');

                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (!IsDigit(next))
                {
                    sb.Append('0'); // ensure a digit follows the decimal point
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private void WarnImprecise(string text, SourcePosition start, SourcePosition end) =>
        _diagnostics.Warning(
            DiagnosticCode.ImpreciseNumber,
            $"Number '{text}' cannot be represented precisely.",
            new SourceSpan(_source, start, end));

    private static bool IsExactlyRepresentable(ulong value)
    {
        const ulong MaxExactInteger = 1UL << 53;
        if (value <= MaxExactInteger)
        {
            return true;
        }

        double approx = value;
        const double TwoPow64 = 18446744073709551616.0;
        if (approx >= TwoPow64)
        {
            return false; // rounded up beyond the 64-bit range
        }

        return (ulong)approx == value;
    }

    // ---------------------------------------------------------------------------------------------
    // Diagnostics for stray characters
    // ---------------------------------------------------------------------------------------------

    private void ReportInvalidCharacter(char c)
    {
        SourcePosition start = CurrentPosition();
        var end = new SourcePosition(start.Offset + 1, start.Line, start.Column + 1);
        var span = new SourceSpan(_source, start, end);

        if (c >= '\u0080')
        {
            _diagnostics.Error(
                DiagnosticCode.NonAsciiCharacter,
                "Non-ASCII character outside string or comment.",
                span);
        }
        else
        {
            _diagnostics.Error(
                DiagnosticCode.UnexpectedCharacter,
                $"Unexpected character '{c}'.",
                span);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Cursor primitives
    // ---------------------------------------------------------------------------------------------

    private bool AtEnd => _index >= _text.Length;

    private char Peek(int ahead = 0)
    {
        int i = _index + ahead;
        return i < _text.Length ? _text[i] : '\0';
    }

    private void Advance()
    {
        char c = _text[_index];
        _index++;
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else if (c != '\r')
        {
            _column++;
        }
    }

    private void AdvanceMany(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Advance();
        }
    }

    private void Restore(int index, int line, int column)
    {
        _index = index;
        _line = line;
        _column = column;
    }

    private SourcePosition CurrentPosition() => new(_index, _line, _column);

    private string Slice(SourcePosition start, SourcePosition end) =>
        _text.Substring(start.Offset, end.Offset - start.Offset);

    // ---------------------------------------------------------------------------------------------
    // Character classes
    // ---------------------------------------------------------------------------------------------

    private static bool IsInlineWhitespace(char c) =>
        // Space, tab, CR, plus U+00A0 NO-BREAK SPACE and U+FEFF BOM (treated as whitespace).
        c is ' ' or '\t' or '\r' or '\u00A0' or '\uFEFF';

    private static bool IsIncludeGapWhitespace(char c) =>
        c is ' ' or '\t' or '\r' or '\n';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsOctalDigit(char c) => c is >= '0' and <= '7';

    private static bool IsHexDigit(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static bool IsIdentifierStart(char c) =>
        c == '_' || c == '$' || c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsIdentifierRest(char c) =>
        c == '_' || c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

    private static int HexValue(char c) =>
        c <= '9' ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10;

    private bool TryReadHex(int offset, int count, out int value)
    {
        value = 0;
        for (int i = 0; i < count; i++)
        {
            char c = Peek(offset + i);
            if (!IsHexDigit(c))
            {
                value = 0;
                return false;
            }

            value = (value << 4) | HexValue(c);
        }

        return true;
    }

    private static bool IsTokenStart(char c)
    {
        if (IsDigit(c) || IsIdentifierStart(c) || c == '"')
        {
            return true;
        }

        return c switch
        {
            '=' or '+' or '-' or '*' or '/' or '%' or '^' => true,
            '<' or '>' or '!' or '~' or '&' or '|' or '#' => true,
            '(' or ')' or '[' or ']' or '{' or '}' => true,
            ';' or ',' or ':' or '.' or '?' => true,
            _ => false,
        };
    }
}
