using System.Text.RegularExpressions;

namespace ScadBundler.IntegrationTests.TestSupport;

/// <summary>
/// Classifies and normalizes OpenSCAD stderr lines for differential comparison: warning-class lines
/// (<c>WARNING:</c>/<c>DEPRECATED:</c>/<c>ERROR:</c>/<c>TRACE:</c>) are compared with their
/// <c>in file …, line N</c> suffix stripped (the bundle has different file names and line numbers);
/// <c>ECHO:</c> lines are compared verbatim, in order.
/// </summary>
internal static partial class OpenScadStderr
{
    private static readonly string[] WarningPrefixes = ["WARNING:", "DEPRECATED:", "ERROR:", "TRACE:"];

    // "… instead. in file assigntest.scad, line 1" / "…, in file C:/x/y.scad, line 12"
    [GeneratedRegex(@",?\s+in file\s+.+?,\s+line\s+\d+\s*\.?\s*$")]
    private static partial Regex FileLineSuffix();

    /// <summary>Whether the line is warning-class (compared after normalization).</summary>
    public static bool IsWarningClass(string line) =>
        Array.Exists(WarningPrefixes, p => line.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Strips the trailing <c>in file …, line N</c> location, if any.</summary>
    public static string Normalize(string line) =>
        FileLineSuffix().Replace(line, string.Empty).TrimEnd();

    /// <summary>The normalized warning-class lines of a stderr capture, in order.</summary>
    public static IReadOnlyList<string> NormalizedWarnings(IEnumerable<string> stderrLines) =>
        [.. stderrLines.Where(IsWarningClass).Select(Normalize)];

    /// <summary>The <c>ECHO:</c> lines of a stderr capture, verbatim, in order.</summary>
    public static IReadOnlyList<string> EchoLines(IEnumerable<string> stderrLines) =>
        [.. stderrLines.Where(l => l.StartsWith("ECHO:", StringComparison.Ordinal))];

    /// <summary>
    /// Multiset difference: every line of <paramref name="bundled"/> not accounted for by an
    /// occurrence in <paramref name="original"/>. Warnings may disappear in the bundle (e.g. the
    /// <c>assign</c>/<c>child</c> deprecations the inliner rewrites away) but never appear.
    /// </summary>
    public static IReadOnlyList<string> NewWarnings(
        IReadOnlyList<string> original, IReadOnlyList<string> bundled)
    {
        Dictionary<string, int> budget = new(StringComparer.Ordinal);
        foreach (string line in original)
        {
            budget[line] = budget.GetValueOrDefault(line) + 1;
        }

        List<string> added = [];
        foreach (string line in bundled)
        {
            if (budget.TryGetValue(line, out int remaining) && remaining > 0)
            {
                budget[line] = remaining - 1;
            }
            else
            {
                added.Add(line);
            }
        }

        return added;
    }
}
