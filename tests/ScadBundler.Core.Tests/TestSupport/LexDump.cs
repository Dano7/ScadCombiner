using System.Globalization;
using System.Text;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Lexing;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// Renders token streams and diagnostics into the canonical golden-master text format used by
/// the corpus fixtures (<c>expected.tokens</c> / <c>expected.diag</c>).
/// </summary>
public static class LexDump
{
    /// <summary>
    /// Renders tokens as <c>&lt;Kind&gt; &lt;line&gt;:&lt;col&gt; &lt;lexeme&gt;</c>, one per line.
    /// The EOF token has no lexeme.
    /// </summary>
    public static string Tokens(IReadOnlyList<Token> tokens)
    {
        var sb = new StringBuilder();
        foreach (Token token in tokens)
        {
            int line = token.Span.Start.Line;
            int column = token.Span.Start.Column;
            if (token.Kind == TokenKind.Eof)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{token.Kind} {line}:{column}");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"{token.Kind} {line}:{column} {token.Text}");
            }

            sb.Append('\n');
        }

        return Normalize(sb.ToString());
    }

    /// <summary>
    /// Renders diagnostics as <c>&lt;Code&gt; &lt;SEVERITY&gt; &lt;line&gt;:&lt;col&gt; &lt;message&gt;</c>,
    /// sorted by position, one per line.
    /// </summary>
    public static string Diagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (Diagnostic diagnostic in diagnostics
            .OrderBy(d => d.Span.Start.Line)
            .ThenBy(d => d.Span.Start.Column))
        {
            string severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "ERROR",
                DiagnosticSeverity.Warning => "WARNING",
                DiagnosticSeverity.Info => "INFO",
                _ => diagnostic.Severity.ToString().ToUpperInvariant(),
            };

            sb.Append(CultureInfo.InvariantCulture,
                $"{diagnostic.Code} {severity} {diagnostic.Span.Start.Line}:{diagnostic.Span.Start.Column} {diagnostic.Message}");
            sb.Append('\n');
        }

        return Normalize(sb.ToString());
    }

    /// <summary>Normalizes line endings and trailing whitespace for golden-master comparison.</summary>
    public static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
}
