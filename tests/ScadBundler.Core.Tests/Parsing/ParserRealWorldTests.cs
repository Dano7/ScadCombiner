using ScadBundler.Core.Parsing;
using ScadBundler.Core.Text;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Parsing;

/// <summary>
/// Real-world coverage (Slice 3 §9): the official OpenSCAD <c>examples/Functions</c> files (vendored
/// as CC0 fixtures under <c>tests/Corpus/slice3-examples</c>) must parse to a complete AST with zero
/// diagnostics. <c>list_comprehensions.scad</c> exercises the comprehension sublanguage end-to-end
/// (multi-binding <c>for</c>, <c>let</c> expressions, nested function literals).
/// </summary>
public sealed class ParserRealWorldTests
{
    private const string SliceFolder = "slice3-examples";

    public static TheoryData<string> Examples()
    {
        var data = new TheoryData<string>();
        string dir = CorpusLocator.SliceDirectory(SliceFolder);
        foreach (string file in Directory.EnumerateFiles(dir, "*.scad").OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void OfficialExample_ParsesWithoutDiagnostics(string fileName)
    {
        string path = Path.Combine(CorpusLocator.SliceDirectory(SliceFolder), fileName);
        ParseResult result = Parser.Parse(new SourceFile(fileName, File.ReadAllText(path)));

        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.Root.Statements);
    }
}
