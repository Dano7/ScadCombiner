using System.Globalization;
using System.Text;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// Golden-master tests over the Slice-4 semantic corpus (<c>tests/Corpus/slice4-semantic</c>). Each
/// case holds an <c>input.scad</c> that parses cleanly and an optional <c>expected.diag</c> (the
/// analyzer's diagnostics; a missing file means "no diagnostics expected"). Diagnostics render as
/// <c>SBnnnn SEVERITY line:col message</c>, per Test-Corpus §4.
/// </summary>
public sealed class Slice4CorpusTests
{
    private const string SliceFolder = "slice4-semantic";

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (string dir in Directory
            .EnumerateDirectories(CorpusLocator.SliceDirectory(SliceFolder))
            .OrderBy(d => d, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(dir));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CorpusCase_MatchesGolden(string caseId)
    {
        string dir = Path.Combine(CorpusLocator.SliceDirectory(SliceFolder), caseId);
        string inputPath = Path.Combine(dir, "input.scad");
        Assert.True(File.Exists(inputPath), $"Missing input.scad for case '{caseId}'.");

        ParseResult parse = Parser.Parse(new SourceFile($"{caseId}/input.scad", File.ReadAllText(inputPath)));
        Assert.Empty(parse.Diagnostics); // semantic cases must parse cleanly

        SemanticResult result = SemanticAnalyzer.Analyze(parse.Root);

        string expectedPath = Path.Combine(dir, "expected.diag");
        string expected = File.Exists(expectedPath) ? Normalize(File.ReadAllText(expectedPath)) : string.Empty;
        Assert.Equal(expected, Format(result.Diagnostics));
    }

    private static string Format(IReadOnlyList<Diagnostic> diagnostics)
    {
        var builder = new StringBuilder();
        foreach (Diagnostic diagnostic in diagnostics)
        {
            builder
                .Append(diagnostic.Code).Append(' ')
                .Append(diagnostic.Severity.ToString().ToUpperInvariant()).Append(' ')
                .Append(diagnostic.Span.Start.Line.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(diagnostic.Span.Start.Column.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(diagnostic.Message).Append('\n');
        }

        return Normalize(builder.ToString());
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
}
