using System.Diagnostics;

namespace ScadBundler.IntegrationTests.TestSupport;

/// <summary>
/// One OpenSCAD invocation's outcome: the exit code and the captured stderr, pre-classified for
/// differential comparison.
/// </summary>
internal sealed record OpenScadRender(int ExitCode, string ScadPath, IReadOnlyList<string> StderrLines)
{
    /// <summary>Exit 0 and no <c>ERROR:</c> line — the render produced trustworthy CSG.</summary>
    public bool Succeeded =>
        ExitCode == 0 && !StderrLines.Any(l => l.StartsWith("ERROR:", StringComparison.Ordinal));

    /// <summary>Warning-class stderr lines with file/line locations stripped.</summary>
    public IReadOnlyList<string> NormalizedWarnings => OpenScadStderr.NormalizedWarnings(StderrLines);

    /// <summary>Verbatim <c>ECHO:</c> lines, in evaluation order.</summary>
    public IReadOnlyList<string> EchoLines => OpenScadStderr.EchoLines(StderrLines);
}

/// <summary>
/// Invokes the official OpenSCAD binary headlessly. <c>-o file.csg</c> stops after CSG-tree
/// generation (no CGAL render), so even real projects finish in seconds, and the CSG text is fully
/// elaborated geometry — bundler renames can never appear in it.
/// </summary>
internal static class OpenScadCli
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(180);

    /// <summary>Renders <paramref name="scadPath"/> to <paramref name="csgPath"/>, capturing stderr.</summary>
    public static OpenScadRender RenderToCsg(string scadPath, string csgPath, string workingDirectory)
    {
        string executable = IntegrationEnvironment.OpenScadExecutable
            ?? throw new InvalidOperationException(
                "OpenSCAD executable not found; this test should have been skipped.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(csgPath);
        startInfo.ArgumentList.Add(scadPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executable}'.");

        // Drain both pipes concurrently; sequential synchronous reads can deadlock on full buffers.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)RenderTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"OpenSCAD render of '{scadPath}' exceeded the 180 s timeout.");
        }

        _ = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        string[] lines = stderr
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new OpenScadRender(process.ExitCode, scadPath, lines);
    }
}
