using ScadBundler.Core.Loading;

namespace ScadBundler.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="IFileSystem"/> that exists but fails on read for paths containing a marker, so the
/// loader's "read error → diagnostic, never throw" paths can be exercised. Delegates everything else
/// to an inner <see cref="InMemoryFileSystem"/>.
/// </summary>
public sealed class FaultyFileSystem(InMemoryFileSystem inner) : IFileSystem
{
    /// <summary>Read of a path containing this marker throws <see cref="IOException"/>.</summary>
    public string IoFailureMarker { get; init; } = "io-fault";

    /// <summary>Read of a path containing this marker throws <see cref="UnauthorizedAccessException"/>.</summary>
    public string DeniedMarker { get; init; } = "denied";

    /// <inheritdoc/>
    public string GetFullPath(string path) => inner.GetFullPath(path);

    /// <inheritdoc/>
    public bool FileExists(string path) => inner.FileExists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    /// <inheritdoc/>
    public string ReadAllText(string path)
    {
        if (path.Contains(IoFailureMarker, StringComparison.Ordinal))
        {
            throw new IOException("simulated read failure");
        }

        if (path.Contains(DeniedMarker, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("simulated access denial");
        }

        return inner.ReadAllText(path);
    }

    /// <inheritdoc/>
    public string? GetDirectoryName(string path) => inner.GetDirectoryName(path);

    /// <inheritdoc/>
    public string Combine(string directory, string relative) => inner.Combine(directory, relative);
}
