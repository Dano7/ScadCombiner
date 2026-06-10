using System.Text;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using Xunit;

namespace ScadBundler.IntegrationTests.TestSupport;

/// <summary>
/// The proven differential recipe: render the original root to CSG with the official binary, bundle
/// it in-process (<see cref="Bundler"/> → <see cref="Emitter"/>, exactly the CLI's wiring), render
/// the bundle from an otherwise-empty directory (which also proves self-containment), then assert
/// the bundle (1) adds no warning-class stderr the original did not have, (2) produces identical
/// <c>ECHO:</c> output, and (3) yields a byte-identical <c>.csg</c>. On failure all artifacts are
/// kept under <c>%TEMP%\ScadBundlerIntegration</c> for triage; on success they are deleted.
/// </summary>
internal static class DifferentialAssert
{
    /// <summary>Runs the full differential loop for one root file.</summary>
    /// <param name="rootPath">The original project's root <c>.scad</c> file.</param>
    /// <param name="options">Bundle options (defaults match the CLI's defaults).</param>
    /// <param name="compareGeometry">
    /// Disable for nondeterministic models (<c>rands()</c>/<c>$t</c>): the warning and ECHO checks
    /// still run, only the CSG byte comparison is skipped.
    /// </param>
    public static void BundleRendersIdentically(
        string rootPath, BundleOptions? options = null, bool compareGeometry = true)
    {
        Assert.True(File.Exists(rootPath), $"Fixture root not found: '{rootPath}'.");

        string label =
            $"{Path.GetFileName(Path.GetDirectoryName(rootPath))}-{Path.GetFileNameWithoutExtension(rootPath)}";
        string workDir = Path.Combine(
            Path.GetTempPath(), "ScadBundlerIntegration", $"{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        // 1 — render the original.
        string originalCsg = Path.Combine(workDir, "original.csg");
        OpenScadRender original = OpenScadCli.RenderToCsg(rootPath, originalCsg, workDir);
        File.WriteAllLines(Path.Combine(workDir, "original.stderr.txt"), original.StderrLines);
        if (!original.Succeeded)
        {
            Assert.Fail(Message(
                $"the ORIGINAL render failed (exit {original.ExitCode}) — fixture, not bundler",
                original.StderrLines, workDir));
        }

        // 2 — bundle in-process. The env-aware overload sees the same OPENSCADPATH as the binary.
        BundleResult bundle = Bundler.Bundle(rootPath, options ?? BundleOptions.Default);
        File.WriteAllLines(
            Path.Combine(workDir, "bundle.diagnostics.txt"),
            bundle.Diagnostics.Select(FormatDiagnostic));
        List<string> errors =
            [.. bundle.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(FormatDiagnostic)];
        if (errors.Count > 0)
        {
            Assert.Fail(Message("bundling produced error diagnostics", errors, workDir));
        }

        // 3 — emit and render the bundle.
        string bundledScad = Path.Combine(workDir, "bundled.scad");
        File.WriteAllText(bundledScad, Emitter.Emit(bundle.Bundled));
        string bundledCsg = Path.Combine(workDir, "bundled.csg");
        OpenScadRender bundled = OpenScadCli.RenderToCsg(bundledScad, bundledCsg, workDir);
        File.WriteAllLines(Path.Combine(workDir, "bundled.stderr.txt"), bundled.StderrLines);
        if (!bundled.Succeeded)
        {
            Assert.Fail(Message(
                $"the BUNDLED render failed (exit {bundled.ExitCode})", bundled.StderrLines, workDir));
        }

        // 4 — no new warning-class stderr (file/line stripped; multiset, so disappearing is fine).
        IReadOnlyList<string> added =
            OpenScadStderr.NewWarnings(original.NormalizedWarnings, bundled.NormalizedWarnings);
        if (added.Count > 0)
        {
            Assert.Fail(Message("the bundle introduced new OpenSCAD warnings", added, workDir));
        }

        // 5 — identical ECHO output, in evaluation order.
        if (!original.EchoLines.SequenceEqual(bundled.EchoLines, StringComparer.Ordinal))
        {
            Assert.Fail(Message(
                "ECHO output diverged",
                [
                    "original:", .. original.EchoLines.Select(l => "  " + l),
                    "bundled:", .. bundled.EchoLines.Select(l => "  " + l),
                ],
                workDir));
        }

        // 6 — byte-identical CSG (fully elaborated geometry).
        if (compareGeometry)
        {
            byte[] expected = File.ReadAllBytes(originalCsg);
            byte[] actual = File.ReadAllBytes(bundledCsg);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(Message(
                    "the CSG output diverged", [FirstDifference(originalCsg, bundledCsg)], workDir));
            }
        }

        TryDelete(workDir); // success: nothing to triage
    }

    private static string FormatDiagnostic(Diagnostic diagnostic) =>
        $"{diagnostic.Severity}: {diagnostic.Code} {diagnostic.Span.File.Path}:{diagnostic.Span.Start.Line}: {diagnostic.Message}";

    private static string FirstDifference(string originalCsg, string bundledCsg)
    {
        string[] original = File.ReadAllLines(originalCsg);
        string[] bundled = File.ReadAllLines(bundledCsg);
        int limit = Math.Min(original.Length, bundled.Length);
        for (int i = 0; i < limit; i++)
        {
            if (!string.Equals(original[i], bundled[i], StringComparison.Ordinal))
            {
                return $"first difference at line {i + 1}: original '{original[i].Trim()}' vs bundled '{bundled[i].Trim()}'";
            }
        }

        return $"line counts differ: original {original.Length} vs bundled {bundled.Length}";
    }

    private static string Message(string headline, IEnumerable<string> details, string workDir)
    {
        var builder = new StringBuilder();
        builder.Append("Differential failure — ").AppendLine(headline);
        foreach (string line in details)
        {
            builder.Append("  ").AppendLine(line);
        }

        builder.Append("Artifacts kept in: ").Append(workDir);
        return builder.ToString();
    }

    private static void TryDelete(string workDir)
    {
        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort — a lingering handle must not fail a passing test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
