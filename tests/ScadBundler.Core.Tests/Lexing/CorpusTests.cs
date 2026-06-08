using ScadBundler.Core.Lexing;
using ScadBundler.Core.Text;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Lexing;

/// <summary>
/// Golden-master tests over the on-disk lexer corpus (<c>tests/Corpus/slice1-lexer</c>). Each case
/// directory holds an <c>input.scad</c> plus an optional <c>expected.tokens</c> and/or
/// <c>expected.diag</c>. A missing <c>expected.diag</c> asserts that no diagnostics are produced.
/// </summary>
public sealed class CorpusTests
{
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        string sliceDir = CorpusLocator.SliceDirectory("slice1-lexer");
        foreach (string dir in Directory.EnumerateDirectories(sliceDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(dir));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CorpusCase_MatchesGolden(string caseId)
    {
        string dir = Path.Combine(CorpusLocator.SliceDirectory("slice1-lexer"), caseId);
        string inputPath = Path.Combine(dir, "input.scad");
        Assert.True(File.Exists(inputPath), $"Missing input.scad for case '{caseId}'.");

        string source = File.ReadAllText(inputPath);
        LexResult result = Lexer.Lex(new SourceFile($"{caseId}/input.scad", source));

        // The lexer always terminates the stream with exactly one EOF token.
        Assert.Equal(TokenKind.Eof, result.Tokens[^1].Kind);
        Assert.Single(result.Tokens, t => t.Kind == TokenKind.Eof);

        string tokensPath = Path.Combine(dir, "expected.tokens");
        if (File.Exists(tokensPath))
        {
            string expected = LexDump.Normalize(File.ReadAllText(tokensPath));
            Assert.Equal(expected, LexDump.Tokens(result.Tokens));
        }

        string diagPath = Path.Combine(dir, "expected.diag");
        if (File.Exists(diagPath))
        {
            string expected = LexDump.Normalize(File.ReadAllText(diagPath));
            Assert.Equal(expected, LexDump.Diagnostics(result.Diagnostics));
        }
        else
        {
            Assert.Empty(result.Diagnostics);
        }
    }
}
