using ScadBundler.Core.Loading;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// A POSIX-style virtual <see cref="IFileSystem"/> for driving the <see cref="SourceLoader"/> in tests
/// without touching disk or the external OpenSCAD checkout. Paths use <c>/</c> separators; relative
/// paths resolve against <see cref="CurrentDirectory"/>. Files are added by absolute key.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>The working directory relative paths resolve against (default <c>/proj</c>).</summary>
    public string CurrentDirectory { get; init; } = "/proj";

    /// <summary>Adds a file at the given (absolute, <c>/</c>-rooted) path. Returns this for chaining.</summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="content">The file text.</param>
    /// <returns>This file system.</returns>
    public InMemoryFileSystem Add(string path, string content)
    {
        _files[GetFullPath(path)] = content;
        return this;
    }

    /// <inheritdoc/>
    public string GetFullPath(string path)
    {
        string rooted = path.Replace('\\', '/');
        if (!rooted.StartsWith('/'))
        {
            rooted = CurrentDirectory.TrimEnd('/') + "/" + rooted;
        }

        var segments = new List<string>();
        foreach (string segment in rooted.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0)
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != "..")
            {
                segments.Add(segment);
            }
        }

        return "/" + string.Join('/', segments);
    }

    /// <inheritdoc/>
    public bool FileExists(string path) => _files.ContainsKey(GetFullPath(path)) || DirectoryExists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path)
    {
        string full = GetFullPath(path);
        string prefix = full == "/" ? "/" : full + "/";
        return _files.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public string ReadAllText(string path) =>
        _files.TryGetValue(GetFullPath(path), out string? content)
            ? content
            : throw new FileNotFoundException(path);

    /// <inheritdoc/>
    public string? GetDirectoryName(string path)
    {
        string full = GetFullPath(path);
        int slash = full.LastIndexOf('/');
        return slash <= 0 ? "/" : full[..slash];
    }

    /// <inheritdoc/>
    public string Combine(string directory, string relative) =>
        relative.Replace('\\', '/').StartsWith('/')
            ? relative
            : directory.TrimEnd('/') + "/" + relative;
}
