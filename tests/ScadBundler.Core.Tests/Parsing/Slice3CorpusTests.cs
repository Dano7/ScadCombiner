using ScadBundler.Core.Parsing;
using ScadBundler.Core.Text;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Golden-master tests over the Slice-3 parser corpus (<c>tests/Corpus/slice3-expr</c>) — the
/// comprehension generators and keyword-prefixed expression forms. Each case directory holds an
/// <c>input.scad</c> and an <c>expected.ast</c> (the canonical <see cref="AstDump"/> serialization).
/// Set the <c>BLESS_AST</c> environment variable to regenerate the golden files via
/// <see cref="Regenerate"/>.
/// </summary>
public sealed class Slice3CorpusTests
{
    private const string SliceFolder = "slice3-expr";

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        string sliceDir = CorpusLocator.SliceDirectory(SliceFolder);
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
        string dir = Path.Combine(CorpusLocator.SliceDirectory(SliceFolder), caseId);
        string inputPath = Path.Combine(dir, "input.scad");
        Assert.True(File.Exists(inputPath), $"Missing input.scad for case '{caseId}'.");

        ParseResult result = Parser.Parse(new SourceFile($"{caseId}/input.scad", File.ReadAllText(inputPath)));

        // Slice 3 introduces no new diagnostics: every corpus case must parse cleanly.
        Assert.Empty(result.Diagnostics);

        string expectedPath = Path.Combine(dir, "expected.ast");
        Assert.True(File.Exists(expectedPath), $"Missing expected.ast for case '{caseId}' (run with BLESS_AST=1 to generate).");

        string expected = AstDump.Normalize(File.ReadAllText(expectedPath));
        Assert.Equal(expected, AstDump.Dump(result.Root));
    }

    [Fact]
    public void Regenerate()
    {
        if (Environment.GetEnvironmentVariable("BLESS_AST") is null)
        {
            return; // generator is opt-in; no-op during normal runs
        }

        string sliceDir = CorpusLocator.SliceDirectory(SliceFolder);
        foreach (string dir in Directory.EnumerateDirectories(sliceDir))
        {
            string inputPath = Path.Combine(dir, "input.scad");
            if (!File.Exists(inputPath))
            {
                continue;
            }

            ParseResult result = Parser.Parse(new SourceFile($"{Path.GetFileName(dir)}/input.scad", File.ReadAllText(inputPath)));
            File.WriteAllText(Path.Combine(dir, "expected.ast"), AstDump.Dump(result.Root) + "\n");
        }
    }
}
