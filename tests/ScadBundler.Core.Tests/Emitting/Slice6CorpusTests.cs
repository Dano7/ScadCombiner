using ScadBundler.Core.Ast;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Tests.TestSupport;
using ScadBundler.Core.Text;
using Xunit;

namespace ScadBundler.Core.Tests.Emitting;

/// <summary>
/// Golden-master tests for the emitter. Two corpora: <c>slice6-emit</c> (parse <c>input.scad</c> →
/// emit, default options) and <c>slice5-bundle</c> (full <see cref="Bundler"/> → emit), each compared
/// to a checked-in <c>expected.scad</c>. A third theory proves idempotence (EM-002):
/// <c>Emit(Parse(expected)) == expected</c> for every golden. Set <c>BLESS_EMIT=1</c> to regenerate the
/// <c>expected.scad</c> files from current emitter output.
/// </summary>
public sealed class Slice6CorpusTests
{
    private static bool Bless => Environment.GetEnvironmentVariable("BLESS_EMIT") == "1";

    // ---------------------------------------------------------------------------------------------
    // slice6-emit: parse input.scad → emit (default) → expected.scad
    // ---------------------------------------------------------------------------------------------

    public static TheoryData<string> EmitCases() => Cases("slice6-emit");

    [Theory]
    [MemberData(nameof(EmitCases))]
    public void EmitCase_MatchesGolden(string caseId)
    {
        string dir = Path.Combine(CorpusLocator.SliceDirectory("slice6-emit"), caseId);
        string source = File.ReadAllText(Path.Combine(dir, "input.scad"));
        ScadFileFromSource(source, out ScadFile root);

        string emitted = Emitter.Emit(root);
        AssertGolden(Path.Combine(dir, "expected.scad"), emitted);
    }

    // ---------------------------------------------------------------------------------------------
    // slice5-bundle: load + analyze + inline (main.scad) → emit (default) → expected.scad
    // ---------------------------------------------------------------------------------------------

    public static TheoryData<string> BundleCases() => Cases("slice5-bundle");

    [Theory]
    [MemberData(nameof(BundleCases))]
    public void BundleCase_MatchesGolden(string caseId)
    {
        string dir = Path.Combine(CorpusLocator.SliceDirectory("slice5-bundle"), caseId);
        BundleResult result = Bundler.Bundle(
            Path.Combine(dir, "main.scad"), ReadOptions(dir), DiskFileSystem.Instance);

        string emitted = Emitter.Emit(result.Bundled);
        AssertGolden(Path.Combine(dir, "expected.scad"), emitted);
    }

    // ---------------------------------------------------------------------------------------------
    // EM-002 idempotence over every golden in both corpora
    // ---------------------------------------------------------------------------------------------

    public static TheoryData<string> AllGoldens()
    {
        var data = new TheoryData<string>();
        foreach (string slice in (string[])["slice6-emit", "slice5-bundle"])
        {
            string root = CorpusLocator.SliceDirectory(slice);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string golden in Directory
                .EnumerateFiles(root, "expected.scad", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal))
            {
                data.Add(golden);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllGoldens))]
    public void Golden_IsIdempotentFixedPoint(string goldenPath)
    {
        string golden = File.ReadAllText(goldenPath);
        ScadFileFromSource(golden, out ScadFile root);
        string reemitted = Emitter.Emit(root);
        Assert.Equal(Normalize(golden), Normalize(reemitted));
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static void ScadFileFromSource(string source, out ScadFile root)
    {
        ParseResult parsed = Parser.Parse(new SourceFile("golden.scad", source));
        root = parsed.Root;
    }

    private static TheoryData<string> Cases(string slice)
    {
        var data = new TheoryData<string>();
        string root = CorpusLocator.SliceDirectory(slice);
        if (!Directory.Exists(root))
        {
            return data;
        }

        foreach (string dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            // slice5-bundle cases drive the emitter only once they carry an expected.scad golden.
            if (string.Equals(slice, "slice5-bundle", StringComparison.Ordinal)
                && !File.Exists(Path.Combine(dir, "expected.scad")) && !Bless)
            {
                continue;
            }

            data.Add(Path.GetFileName(dir));
        }

        return data;
    }

    private static BundleOptions ReadOptions(string dir)
    {
        string optionsPath = Path.Combine(dir, "options.txt");
        CollisionStrategy strategy = CollisionStrategy.Auto;
        if (File.Exists(optionsPath))
        {
            string[] tokens = File.ReadAllLines(optionsPath);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Trim() == "--on-collision" && i + 1 < tokens.Length)
                {
                    strategy = Enum.Parse<CollisionStrategy>(tokens[i + 1].Trim(), ignoreCase: true);
                }
            }
        }

        return new BundleOptions([dir], strategy);
    }

    private static void AssertGolden(string expectedPath, string emitted)
    {
        if (Bless)
        {
            File.WriteAllText(expectedPath, emitted);
            return;
        }

        Assert.True(File.Exists(expectedPath), $"Missing golden '{expectedPath}' (run with BLESS_EMIT=1 to generate).");
        Assert.Equal(Normalize(File.ReadAllText(expectedPath)), Normalize(emitted));
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
}
