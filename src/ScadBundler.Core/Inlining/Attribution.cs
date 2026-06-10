using ScadBundler.Core.Ast;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Inlining;

/// <summary>
/// The bundle-attribution pass behind <c>--bundle-licenses</c> (default on). Collects each loaded
/// file's leading header/license comments in <c>include</c>/<c>use</c> encounter order (root first),
/// deduplicates them by normalized text, and exposes: the aggregated header block hoisted to the top
/// of the bundle, the set of hoisted trivia to strip from their original statements (moved, not
/// copied), and per-file provenance labels (<c>include &lt;…&gt;</c> / <c>use &lt;…&gt;</c>, echoing
/// the author's original statement) used for the one-line section banners. A header run stops at the
/// first Customizer group marker (<c>/* [Name] */</c>) — group markers belong to the parameter that
/// follows them, never to the license block, so the Customizer UI is unaffected by hoisting.
/// </summary>
internal sealed class Attribution
{
    private const string BlockOpen =
        "// ======== file headers & licenses aggregated by ScadBundler ========";

    private const string BlockClose =
        "// ====================================================================";

    private readonly HashSet<Trivia> _hoisted = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SourceFile, string> _labels = [];
    private readonly List<Trivia> _headerBlock = [];

    private Attribution()
    {
    }

    /// <summary>The aggregated header block: the root's own header first (unframed — it is the
    /// author's top-of-file header), then every distinct non-root header inside a delimited block.
    /// Empty when no bundled file carries a header.</summary>
    public IReadOnlyList<Trivia> HeaderBlock => _headerBlock;

    /// <summary>How many non-root file headers were aggregated into the block (the SB5007 count).</summary>
    public int AggregatedHeaderCount { get; private set; }

    /// <summary>Walks <paramref name="graph"/> from the root in encounter order — each file's
    /// <c>include</c>/<c>use</c> edges in source position order, depth-first — collecting and
    /// deduplicating header runs and assigning every file its provenance label.</summary>
    /// <param name="graph">The loaded graph to collect from.</param>
    /// <returns>The collected attribution state.</returns>
    public static Attribution Collect(LoadGraph graph)
    {
        var result = new Attribution();
        var seenFiles = new HashSet<SourceFile>();
        var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
        var rootRun = new List<Trivia>();
        var entries = new List<(string Label, List<Trivia> Run)>();

        void Visit(LoadedFile file, string label, bool isRoot)
        {
            if (!seenFiles.Add(file.Source))
            {
                return; // first encounter wins (a diamond is loaded once anyway)
            }

            result._labels[file.Source] = label;

            List<Trivia> run = HeaderRun(file.Ast);
            if (run.Count > 0)
            {
                // Always strip a collected header from its original statement; add it to the block
                // only when its text is new (identical headers across files appear once).
                foreach (Trivia trivia in run)
                {
                    result._hoisted.Add(trivia);
                }

                if (seenHeaders.Add(NormalizedText(run)))
                {
                    if (isRoot)
                    {
                        rootRun.AddRange(run);
                    }
                    else
                    {
                        entries.Add((label, run));
                    }
                }
            }

            foreach ((Statement statement, LoadedFile target, bool isUse) in OrderedEdges(file))
            {
                string targetLabel = isUse
                    ? $"use <{((UseStatement)statement).RawPath}>"
                    : $"include <{((IncludeStatement)statement).RawPath}>";
                Visit(target, targetLabel, isRoot: false);
            }
        }

        Visit(graph.Root, Path.GetFileName(graph.Root.Source.Path), isRoot: true);

        result._headerBlock.AddRange(rootRun);
        if (entries.Count > 0)
        {
            result._headerBlock.Add(Line(BlockOpen));
            foreach ((string label, List<Trivia> run) in entries)
            {
                result._headerBlock.Add(Line($"// -------- {label} --------"));
                result._headerBlock.AddRange(run);
            }

            result._headerBlock.Add(Line(BlockClose));
            result.AggregatedHeaderCount = entries.Count;
        }

        return result;
    }

    /// <summary>Whether <paramref name="trivia"/> was hoisted into the header block and must be
    /// stripped from the statement it originally rode on.</summary>
    /// <param name="trivia">The trivia to test (by reference).</param>
    /// <returns><c>true</c> when the trivia was hoisted.</returns>
    public bool IsHoisted(Trivia trivia) => _hoisted.Contains(trivia);

    /// <summary>The provenance label for <paramref name="file"/>: the root's file name, or the
    /// <c>include &lt;…&gt;</c>/<c>use &lt;…&gt;</c> form of the statement that first pulled the
    /// file in. Falls back to the file name for a file never seen by <see cref="Collect"/>.</summary>
    /// <param name="file">The source file to label.</param>
    /// <returns>The display label.</returns>
    public string LabelFor(SourceFile file) =>
        _labels.TryGetValue(file, out string? label) ? label : Path.GetFileName(file.Path);

    // The file's header run: the leading comments of its first statement (or the EOF trivia of a
    // comments-only file), cut at the first Customizer group marker.
    private static List<Trivia> HeaderRun(ScadFile ast)
    {
        IReadOnlyList<Trivia> source = ast.Statements.Count > 0
            ? ast.Statements[0].LeadingTrivia
            : ast.TrailingTrivia;

        var run = new List<Trivia>();
        foreach (Trivia trivia in source)
        {
            if (IsCustomizerGroupMarker(trivia))
            {
                break;
            }

            run.Add(trivia);
        }

        return run;
    }

    // A Customizer group marker: a single-line block comment whose content is exactly "[ ... ]"
    // (e.g. "/* [Box] */" or the synthesized "/* [Hidden] */").
    private static bool IsCustomizerGroupMarker(Trivia trivia)
    {
        if (trivia is not CommentTrivia { Kind: CommentKind.Block } comment || comment.Text.Length < 4)
        {
            return false;
        }

        string inner = comment.Text[2..^2].Trim();
        return inner.Length >= 2
            && inner[0] == '['
            && inner[^1] == ']'
            && !inner.Contains('\n', StringComparison.Ordinal);
    }

    private static string NormalizedText(List<Trivia> run) =>
        string.Join('\n', run.OfType<CommentTrivia>()
            .Select(c => c.Text.ReplaceLineEndings("\n").Trim()));

    // Edges in source position order, so traversal mirrors the order a reader (and OpenSCAD)
    // encounters the include/use statements. Unresolved and font edges carry no loaded file.
    private static List<(Statement Statement, LoadedFile Target, bool IsUse)> OrderedEdges(LoadedFile file)
    {
        var edges = new List<(Statement Statement, LoadedFile Target, bool IsUse)>();
        foreach (IncludeEdge edge in file.Includes)
        {
            if (edge.Target is not null)
            {
                edges.Add((edge.Statement, edge.Target, false));
            }
        }

        foreach (UseEdge edge in file.Uses)
        {
            if (edge.Target is not null && !edge.FontPassthrough)
            {
                edges.Add((edge.Statement, edge.Target, true));
            }
        }

        edges.Sort((a, b) => a.Statement.Span.Start.Offset.CompareTo(b.Statement.Span.Start.Offset));
        return edges;
    }

    private static CommentTrivia Line(string text) =>
        new(text, CommentKind.Line) { Span = SourceSpan.Synthetic };
}
