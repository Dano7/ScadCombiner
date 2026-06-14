using System.Globalization;
using System.Text;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// Golden-master tests over the Slice-5 bundle corpus (<c>tests/Corpus/slice5-bundle</c>), driving the
/// real <see cref="Bundler"/> end-to-end (loader → analyzer → inliner) on multi-file fixtures from
/// disk. Each case has <c>main.scad</c> (+ libs), an optional <c>options.txt</c> (one CLI token per
/// line), and an optional <c>expected.diag</c> (absent = no diagnostics). Diagnostics render as
/// <c>SBnnnn SEVERITY file:line:col message</c> with paths made relative to the case directory.
/// </summary>
public sealed class Slice5CorpusTests
{
    private const string SliceFolder = "slice5-bundle";

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
        string rootPath = Path.Combine(dir, "main.scad");
        Assert.True(File.Exists(rootPath), $"Missing main.scad for case '{caseId}'.");

        BundleResult result = Bundler.Bundle(rootPath, ReadOptions(dir), DiskFileSystem.Instance);

        string expectedPath = Path.Combine(dir, "expected.diag");
        string expected = File.Exists(expectedPath) ? Normalize(File.ReadAllText(expectedPath)) : string.Empty;
        Assert.Equal(expected, Format(result.Diagnostics, dir));
    }

    private static BundleOptions ReadOptions(string dir)
    {
        string optionsPath = Path.Combine(dir, "options.txt");
        CollisionStrategy strategy = CollisionStrategy.Auto;
        bool lint = false;
        if (File.Exists(optionsPath))
        {
            string[] tokens = File.ReadAllLines(optionsPath);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token == "--on-collision" && i + 1 < tokens.Length)
                {
                    strategy = Enum.Parse<CollisionStrategy>(tokens[i + 1].Trim(), ignoreCase: true);
                }
                else if (token == "--lint")
                {
                    lint = true; // surface SB3004/SB3005 static-lint findings (suppressed by default)
                }
            }
        }

        return new BundleOptions([dir], strategy, Lint: lint);
    }

    private static string Format(IReadOnlyList<Diagnostic> diagnostics, string caseDir)
    {
        var builder = new StringBuilder();
        foreach (Diagnostic diagnostic in diagnostics)
        {
            builder
                .Append(diagnostic.Code).Append(' ')
                .Append(diagnostic.Severity.ToString().ToUpperInvariant()).Append(' ')
                .Append(Relativize(diagnostic.Span.File.Path, caseDir)).Append(':')
                .Append(diagnostic.Span.Start.Line.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(diagnostic.Span.Start.Column.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(diagnostic.Message).Append('\n');
        }

        return Normalize(builder.ToString());
    }

    private static string Relativize(string path, string caseDir)
    {
        string normalized = path.Replace('\\', '/');
        string prefix = caseDir.Replace('\\', '/').TrimEnd('/') + "/";
        return normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : normalized;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
}
