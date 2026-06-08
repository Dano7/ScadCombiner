namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// Locates the on-disk test corpus (<c>tests/Corpus/…</c>) relative to the running test assembly,
/// by walking up to the directory that contains <c>ScadBundler.sln</c>.
/// </summary>
public static class CorpusLocator
{
    private static readonly Lazy<string> RepoRootValue = new(FindRepoRoot);

    /// <summary>The repository root (the directory containing <c>ScadBundler.sln</c>).</summary>
    public static string RepoRoot => RepoRootValue.Value;

    /// <summary>The absolute path of a corpus slice directory, e.g. <c>slice1-lexer</c>.</summary>
    public static string SliceDirectory(string sliceFolder) =>
        Path.Combine(RepoRoot, "tests", "Corpus", sliceFolder);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ScadBundler.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate ScadBundler.sln above '{AppContext.BaseDirectory}'.");
    }
}
