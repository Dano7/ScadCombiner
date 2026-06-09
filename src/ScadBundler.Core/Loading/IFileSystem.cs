namespace ScadBundler.Core.Loading;

/// <summary>
/// The file-access seam the <see cref="SourceLoader"/> reads through, so loading can be driven from
/// disk in production and from in-memory fixtures in tests (no dependency on any external checkout).
/// Implementations do pure path manipulation plus existence/read; the loader owns all policy
/// (search order, caching, cycle detection).
/// </summary>
public interface IFileSystem
{
    /// <summary>Canonicalizes <paramref name="path"/> to a stable absolute form used as the cache and
    /// cycle-detection key (equivalent paths must canonicalize identically).</summary>
    /// <param name="path">The path to canonicalize.</param>
    /// <returns>The canonical absolute path.</returns>
    string GetFullPath(string path);

    /// <summary>True when <paramref name="path"/> exists (file or directory).</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><c>true</c> when something exists at the path.</returns>
    bool FileExists(string path);

    /// <summary>True when <paramref name="path"/> exists and is a directory (skipped by path resolution,
    /// which takes the first existing <i>non-directory</i> match).</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><c>true</c> when the path is an existing directory.</returns>
    bool DirectoryExists(string path);

    /// <summary>Reads the full text of <paramref name="path"/> (UTF-8).</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The decoded file text.</returns>
    string ReadAllText(string path);

    /// <summary>The directory portion of <paramref name="path"/>, or <c>null</c> when it has none.</summary>
    /// <param name="path">The path whose directory is wanted.</param>
    /// <returns>The directory, or <c>null</c>.</returns>
    string? GetDirectoryName(string path);

    /// <summary>Joins <paramref name="directory"/> and <paramref name="relative"/> into one path.</summary>
    /// <param name="directory">The base directory.</param>
    /// <param name="relative">The relative path to append.</param>
    /// <returns>The combined path.</returns>
    string Combine(string directory, string relative);
}

/// <summary>The production <see cref="IFileSystem"/>, delegating to <see cref="System.IO"/>.</summary>
public sealed class DiskFileSystem : IFileSystem
{
    /// <summary>A shared instance (the file system is stateless).</summary>
    public static readonly DiskFileSystem Instance = new();

    /// <inheritdoc/>
    public string GetFullPath(string path) => Path.GetFullPath(path);

    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc/>
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    /// <inheritdoc/>
    public string Combine(string directory, string relative) => Path.Combine(directory, relative);
}
