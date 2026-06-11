using System.Globalization;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Lexing;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Transforming;

/// <summary>
/// Minify literal re-spelling (§6.3): replaces each <see cref="NumberLiteral"/>'s raw text with the
/// shortest string that re-lexes to the <b>bit-identical</b> double (<c>1.0</c>→<c>1</c>,
/// <c>0.5</c>→<c>.5</c>, <c>0x10</c>→<c>16</c>). No arithmetic is performed — this is re-spelling, not
/// folding; every candidate is verified by re-lexing it and comparing the decoded double's bits, so the
/// value feeding any geometry parameter is provably unchanged. When no shorter exact spelling exists the
/// original text is kept.
/// </summary>
internal sealed class LiteralCanonicalization : IBundleTransform
{
    /// <inheritdoc/>
    public string Name => "literal-canonicalization";

    /// <inheritdoc/>
    public bool NeedsModel => false;

    /// <inheritdoc/>
    public ScadFile Apply(ScadFile bundle, TransformContext context) => new Canonicalizer().Rewrite(bundle);

    private sealed class Canonicalizer : TreeRewriter
    {
        protected override Expression Transform(Expression rebuilt)
        {
            if (rebuilt is not NumberLiteral number)
            {
                return rebuilt;
            }

            string? shorter = Shortest(number.Value, number.RawText);
            return shorter is null ? number : number with { RawText = shorter };
        }
    }

    // The shortest verified re-spelling strictly shorter than the original, or null to keep the original.
    private static string? Shortest(double value, string original)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return null; // negatives are Unary(Negate, …); inf/nan have no safe shorter literal
        }

        string? best = null;
        foreach (string candidate in Candidates(value))
        {
            if (candidate.Length < original.Length
                && (best is null || candidate.Length < best.Length)
                && ReLexesTo(candidate, value))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IEnumerable<string> Candidates(double value)
    {
        string round = value.ToString(CultureInfo.InvariantCulture); // shortest round-trippable decimal
        yield return round;
        if (round.StartsWith("0.", StringComparison.Ordinal))
        {
            yield return round[1..]; // 0.5 -> .5
        }
    }

    // True when <paramref name="candidate"/> lexes to exactly one Number token whose decoded double has
    // the same bits as <paramref name="target"/> (and produced no lexer diagnostics).
    private static bool ReLexesTo(string candidate, double target)
    {
        LexResult result = Lexer.Lex(new SourceFile("<num>", candidate));
        if (result.Diagnostics.Count > 0)
        {
            return false;
        }

        Token? single = null;
        foreach (Token token in result.Tokens)
        {
            if (token.Kind == TokenKind.Eof)
            {
                continue;
            }

            if (single is not null)
            {
                return false; // more than one significant token
            }

            single = token;
        }

        return single is { Kind: TokenKind.Number, NumberValue: { } value }
            && BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(target);
    }
}
