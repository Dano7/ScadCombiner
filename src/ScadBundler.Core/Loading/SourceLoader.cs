using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Loading;

/// <summary>
/// Resolves a root <c>.scad</c> path and every file reachable from it via <c>include</c>/<c>use</c>
/// into a <see cref="LoadGraph"/>: each file is lexed+parsed once (cached by absolute path, so a file
/// shared by many paths — a DAG diamond — is loaded once) and its edges resolved per the Spec "File
/// Resolution" order. A path on the active ancestry stack is a cycle (<c>SB4002</c>) and is not
/// recursed into; an unresolvable path is <c>SB4001</c> with a <c>null</c> target. The loader
/// <b>never throws</b> — missing files, read errors, and parse errors all become diagnostics.
/// </summary>
public sealed class SourceLoader
{
    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<string> _libraryPaths;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly Dictionary<string, LoadedFile> _byAbsolutePath = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    private SourceLoader(IFileSystem fileSystem, IReadOnlyList<string> libraryPaths)
    {
        _fs = fileSystem;
        _libraryPaths = libraryPaths;
    }

    /// <summary>Loads <paramref name="rootPath"/> and its dependency closure. Never throws.</summary>
    /// <param name="rootPath">The root <c>.scad</c> file to bundle from.</param>
    /// <param name="options">Bundle options (supplies the extra <see cref="BundleOptions.LibraryPaths"/>).</param>
    /// <param name="fileSystem">The file-access seam (disk in production, in-memory in tests).</param>
    /// <returns>The populated load graph.</returns>
    public static LoadGraph Load(string rootPath, BundleOptions options, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        return new SourceLoader(fileSystem, options.LibraryPaths).Run(rootPath);
    }

    private LoadGraph Run(string rootPath)
    {
        string rootAbs = SafeFullPath(rootPath);
        LoadedFile root = LoadFile(rootAbs, rootPath) ?? UnreadableRoot(rootPath, rootAbs);
        return new LoadGraph(root, _byAbsolutePath, _diagnostics.ToList());
    }

    // Loads (and caches post-order) the file at the given absolute path, or returns null when it
    // cannot be read. Cycle/diamond bookkeeping is handled by the edge resolvers below.
    private LoadedFile? LoadFile(string absolutePath, string displayPath)
    {
        if (_byAbsolutePath.TryGetValue(absolutePath, out LoadedFile? cached))
        {
            return cached; // diamond / DAG: load once
        }

        if (!TryRead(absolutePath, out string text))
        {
            return null;
        }

        var source = new SourceFile(displayPath, text);
        ParseResult parse = Parser.Parse(source);
        foreach (Diagnostic diagnostic in parse.Diagnostics)
        {
            _diagnostics.Report(diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Span);
        }

        _active.Add(absolutePath);
        string includerDir = _fs.GetDirectoryName(absolutePath) ?? string.Empty;
        var includes = new List<IncludeEdge>();
        var uses = new List<UseEdge>();
        foreach (Statement statement in parse.Root.Statements)
        {
            switch (statement)
            {
                case IncludeStatement include:
                    includes.Add(ResolveInclude(include, includerDir));
                    break;
                case UseStatement use:
                    uses.Add(ResolveUse(use, includerDir));
                    break;
            }
        }

        _active.Remove(absolutePath);

        var loaded = new LoadedFile(source, parse.Root, includes, uses);
        _byAbsolutePath[absolutePath] = loaded;
        return loaded;
    }

    private IncludeEdge ResolveInclude(IncludeStatement statement, string includerDir)
    {
        string? absolutePath = ResolvePath(statement.RawPath, includerDir);
        if (absolutePath is null)
        {
            ReportNotFound(statement.RawPath, statement.Span);
            return new IncludeEdge(statement, null);
        }

        if (_active.Contains(absolutePath))
        {
            ReportCycle(statement.RawPath, statement.Span);
            return new IncludeEdge(statement, null);
        }

        LoadedFile? target = LoadFile(absolutePath, statement.RawPath);
        if (target is null)
        {
            ReportNotFound(statement.RawPath, statement.Span);
            return new IncludeEdge(statement, null);
        }

        return new IncludeEdge(statement, target);
    }

    private UseEdge ResolveUse(UseStatement statement, string includerDir)
    {
        if (IsFont(statement.RawPath))
        {
            return new UseEdge(statement, null, FontPassthrough: true);
        }

        string? absolutePath = ResolvePath(statement.RawPath, includerDir);
        if (absolutePath is null)
        {
            ReportNotFound(statement.RawPath, statement.Span);
            return new UseEdge(statement, null);
        }

        if (_active.Contains(absolutePath))
        {
            ReportCycle(statement.RawPath, statement.Span);
            return new UseEdge(statement, null);
        }

        LoadedFile? target = LoadFile(absolutePath, statement.RawPath);
        if (target is null)
        {
            ReportNotFound(statement.RawPath, statement.Span);
            return new UseEdge(statement, null);
        }

        return new UseEdge(statement, target);
    }

    // File-resolution order (Spec "File Resolution", mirroring `parsersettings.cc` find_valid_path):
    // including file's directory → each library path in order. First existing non-directory wins.
    private string? ResolvePath(string rawPath, string includerDir)
    {
        if (rawPath.Length == 0)
        {
            return null;
        }

        string normalized = rawPath.Replace('\\', '/');
        if (IsAbsolute(normalized))
        {
            return ExistsAsFile(normalized) ? _fs.GetFullPath(normalized) : null;
        }

        string fromIncluder = _fs.Combine(includerDir, normalized);
        if (ExistsAsFile(fromIncluder))
        {
            return _fs.GetFullPath(fromIncluder);
        }

        foreach (string libraryPath in _libraryPaths)
        {
            string candidate = _fs.Combine(libraryPath, normalized);
            if (ExistsAsFile(candidate))
            {
                return _fs.GetFullPath(candidate);
            }
        }

        return null;
    }

    private bool ExistsAsFile(string path) => _fs.FileExists(path) && !_fs.DirectoryExists(path);

    private bool TryRead(string absolutePath, out string text)
    {
        if (!_fs.FileExists(absolutePath) || _fs.DirectoryExists(absolutePath))
        {
            text = string.Empty;
            return false;
        }

        try
        {
            text = _fs.ReadAllText(absolutePath);
            return true;
        }
        catch (IOException)
        {
            text = string.Empty;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            text = string.Empty;
            return false;
        }
    }

    private string SafeFullPath(string path)
    {
        try
        {
            return _fs.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    // A root that could not be read still yields a (empty) graph node so the pipeline never throws.
    private LoadedFile UnreadableRoot(string displayPath, string absolutePath)
    {
        ReportNotFound(displayPath, SyntheticSpan(displayPath));
        var source = new SourceFile(displayPath, string.Empty);
        var empty = new LoadedFile(source, new ScadFile(source, []), [], []);
        _byAbsolutePath[absolutePath] = empty;
        return empty;
    }

    private void ReportNotFound(string rawPath, SourceSpan span) =>
        _diagnostics.Warning(
            DiagnosticCode.IncludeUseNotFound,
            $"Can't find '{rawPath}' on the search path; statement ignored.",
            span);

    private void ReportCycle(string rawPath, SourceSpan span) =>
        _diagnostics.Error(
            DiagnosticCode.CircularReference,
            $"Circular reference: '{rawPath}' is already being processed.",
            span);

    private static SourceSpan SyntheticSpan(string displayPath)
    {
        var file = new SourceFile(displayPath, string.Empty);
        return new SourceSpan(file, default, default);
    }

    private static bool IsAbsolute(string path) => path.StartsWith('/') || Path.IsPathRooted(path);

    private static bool IsFont(string rawPath) =>
        rawPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
        || rawPath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);
}
