using System.Globalization;
using System.Text;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;

namespace ScadBundler.Cli;

/// <summary>
/// Implements <c>scadbundler bundle &lt;input.scad&gt; [options]</c>: parses arguments, runs the full
/// pipeline (<see cref="Bundler"/> → <see cref="Emitter"/>), prints diagnostics, and writes the bundle.
/// Returns the process exit code (0 success, 1 error-severity diagnostics, 2 usage/argument error).
/// </summary>
internal static class BundleCommand
{
    /// <summary>Runs the CLI against pre-split <paramref name="args"/>, writing to the given streams.</summary>
    /// <param name="args">The process arguments (excluding the executable name).</param>
    /// <param name="stdout">The standard-output stream (bundle text / summaries / diffs).</param>
    /// <param name="stderr">The standard-error stream (diagnostics / usage / verbose log).</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 0 || IsHelpFlag(args[0]))
        {
            (args.Length == 0 ? stderr : stdout).Write(UsageText);
            return args.Length == 0 ? 2 : 0;
        }

        if (!string.Equals(args[0], "bundle", StringComparison.Ordinal))
        {
            stderr.WriteLine($"error: unknown command '{args[0]}'.");
            stderr.Write(UsageText);
            return 2;
        }

        if (!TryParse(args, out Options options, out string? error, out bool help))
        {
            if (help)
            {
                stdout.Write(UsageText);
                return 0;
            }

            stderr.WriteLine($"error: {error}");
            stderr.Write(UsageText);
            return 2;
        }

        if (!File.Exists(options.Input))
        {
            stderr.WriteLine($"error: cannot read input file '{options.Input}'.");
            return 1;
        }

        var bundleOptions = new BundleOptions(
            [.. options.LibraryPaths, .. OpenScadEnvironment.LibraryPaths()],
            options.OnCollision,
            options.BundleLicenses,
            options.PreserveComments);
        var emitOptions = new EmitOptions(
            Minify: options.Minify,
            PreserveComments: options.PreserveComments && !options.Minify);

        BundleResult result = Bundler.Bundle(options.Input, bundleOptions, DiskFileSystem.Instance);
        PrintDiagnostics(result.Diagnostics, stderr);

        if (result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return 1; // error-severity diagnostics: no output produced
        }

        if (options.Verbose)
        {
            stderr.WriteLine(VerboseSummary(options.Input, bundleOptions, result.Diagnostics));
        }

        string emitted = Emitter.Emit(result.Bundled, emitOptions);
        string outputPath = options.Output ?? DefaultOutputPath(options.Input);

        if (options.DryRun)
        {
            stdout.WriteLine(
                $"dry-run: would write {Count(result.Bundled.Statements.Count, "statement")} to "
                + $"'{(options.Output == "-" ? "<stdout>" : outputPath)}'.");
            return 0;
        }

        if (options.Diff)
        {
            stdout.Write(UnifiedDiff(File.ReadAllText(options.Input), emitted, options.Input, outputPath));
            return 0;
        }

        if (options.Output == "-")
        {
            stdout.Write(emitted);
        }
        else
        {
            File.WriteAllText(outputPath, emitted);
            if (options.Verbose)
            {
                stderr.WriteLine($"wrote '{outputPath}'.");
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------------------------------------
    // Argument parsing
    // ---------------------------------------------------------------------------------------------

    private sealed class Options
    {
        public string Input { get; set; } = string.Empty;

        public string? Output { get; set; }

        public List<string> LibraryPaths { get; } = [];

        public CollisionStrategy OnCollision { get; set; } = CollisionStrategy.Auto;

        public bool BundleLicenses { get; set; } = true;

        public bool PreserveComments { get; set; } = true;

        public bool Minify { get; set; }

        public bool DryRun { get; set; }

        public bool Diff { get; set; }

        public bool Verbose { get; set; }
    }

    private static bool TryParse(string[] args, out Options options, out string? error, out bool help)
    {
        options = new Options();
        error = null;
        help = false;
        bool haveInput = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    help = true;
                    return false;
                case "-o" or "--output":
                    if (!TryValue(args, ref i, arg, out string? output, out error))
                    {
                        return false;
                    }

                    options.Output = output;
                    break;
                case "-p" or "--library-path":
                    if (!TryValue(args, ref i, arg, out string? paths, out error))
                    {
                        return false;
                    }

                    options.LibraryPaths.AddRange(paths!.Split(
                        ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--on-collision":
                    if (!TryValue(args, ref i, arg, out string? strategy, out error))
                    {
                        return false;
                    }

                    if (!TryParseCollision(strategy!, out CollisionStrategy parsed))
                    {
                        error = $"invalid --on-collision value '{strategy}' (expected auto|prefix|error|keep-first|keep-last).";
                        return false;
                    }

                    options.OnCollision = parsed;
                    break;
                case "--bundle-licenses":
                    options.BundleLicenses = true;
                    break;
                case "--no-bundle-licenses":
                    options.BundleLicenses = false;
                    break;
                case "--preserve-comments":
                    options.PreserveComments = true;
                    break;
                case "--no-preserve-comments":
                    options.PreserveComments = false;
                    break;
                case "--minify":
                    options.Minify = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--diff":
                    options.Diff = true;
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"unknown option '{arg}'.";
                        return false;
                    }

                    if (haveInput)
                    {
                        error = $"unexpected extra argument '{arg}'.";
                        return false;
                    }

                    options.Input = arg;
                    haveInput = true;
                    break;
            }
        }

        if (!haveInput)
        {
            error = "no input file specified.";
            return false;
        }

        return true;
    }

    private static bool TryValue(string[] args, ref int i, string option, out string? value, out string? error)
    {
        if (i + 1 >= args.Length)
        {
            value = null;
            error = $"option '{option}' requires a value.";
            return false;
        }

        value = args[++i];
        error = null;
        return true;
    }

    private static bool TryParseCollision(string value, out CollisionStrategy strategy)
    {
        switch (value)
        {
            case "auto": strategy = CollisionStrategy.Auto; return true;
            case "prefix": strategy = CollisionStrategy.Prefix; return true;
            case "error": strategy = CollisionStrategy.Error; return true;
            case "keep-first": strategy = CollisionStrategy.KeepFirst; return true;
            case "keep-last": strategy = CollisionStrategy.KeepLast; return true;
            default: strategy = CollisionStrategy.Auto; return false;
        }
    }

    private static bool IsHelpFlag(string arg) =>
        arg is "-h" or "--help" or "help";

    // ---------------------------------------------------------------------------------------------
    // Output helpers
    // ---------------------------------------------------------------------------------------------

    private static void PrintDiagnostics(IReadOnlyList<Diagnostic> diagnostics, TextWriter stderr)
    {
        foreach (DiagnosticSeverity severity in
            (ReadOnlySpan<DiagnosticSeverity>)[DiagnosticSeverity.Error, DiagnosticSeverity.Warning, DiagnosticSeverity.Info])
        {
            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == severity)
                {
                    stderr.WriteLine(FormatDiagnostic(diagnostic));
                }
            }
        }
    }

    private static string FormatDiagnostic(Diagnostic diagnostic)
    {
        var builder = new StringBuilder();
        builder
            .Append(diagnostic.Severity.ToString().ToUpperInvariant()).Append(": ")
            .Append(diagnostic.Code).Append(' ')
            .Append(diagnostic.Span.File.Path).Append(':')
            .Append(diagnostic.Span.Start.Line.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(diagnostic.Span.Start.Column.ToString(CultureInfo.InvariantCulture)).Append(": ")
            .Append(diagnostic.Message);
        return builder.ToString();
    }

    private static string VerboseSummary(string input, BundleOptions options, IReadOnlyList<Diagnostic> diagnostics)
    {
        LoadGraph graph = SourceLoader.Load(input, options, DiskFileSystem.Instance);
        string rootPath = graph.Root.Source.Path;
        List<string> inlined =
        [
            .. graph.ByAbsolutePath.Values
                .Select(f => f.Source.Path)
                .Where(p => !string.Equals(p, rootPath, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal),
        ];

        int renames = diagnostics.Count(d => d.Code == DiagnosticCode.NameRenamed);
        int normalizations = diagnostics.Count(d =>
            d.Code is DiagnosticCode.AssignNormalized or DiagnosticCode.ChildNormalized);

        string files = inlined.Count == 0 ? "none" : string.Join(", ", inlined);
        return $"{Count(inlined.Count, "file")} inlined ({files}), "
            + $"{Count(renames, "rename")}, {Count(normalizations, "normalization")}";
    }

    private static string Count(int n, string noun) =>
        $"{n.ToString(CultureInfo.InvariantCulture)} {noun}{(n == 1 ? string.Empty : "s")}";

    private static string DefaultOutputPath(string input)
    {
        string directory = Path.GetDirectoryName(input) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(input);
        return Path.Combine(directory, stem + ".bundled.scad");
    }

    // A compact line-based unified diff (LCS); used by --diff to preview the bundle against its root.
    private static string UnifiedDiff(string oldText, string newText, string oldName, string newName)
    {
        string[] a = SplitLines(oldText);
        string[] b = SplitLines(newText);
        int[,] lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var builder = new StringBuilder();
        builder.Append("--- ").Append(oldName).Append('\n');
        builder.Append("+++ ").Append(newName).Append('\n');
        int x = 0;
        int y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                builder.Append(' ').Append(a[x++]).Append('\n');
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                builder.Append('-').Append(a[x++]).Append('\n');
            }
            else
            {
                builder.Append('+').Append(b[y++]).Append('\n');
            }
        }

        while (x < a.Length)
        {
            builder.Append('-').Append(a[x++]).Append('\n');
        }

        while (y < b.Length)
        {
            builder.Append('+').Append(b[y++]).Append('\n');
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private const string UsageText =
        """
        scadbundler — AST-based OpenSCAD file bundler

        Usage:
          scadbundler bundle <input.scad> [options]

        Options:
          -o, --output <file>        Output path (default <input>.bundled.scad; '-' = stdout)
          -p, --library-path <p>     Extra search path (repeatable or comma-separated)
          --on-collision <strategy>  auto|prefix|error|keep-first|keep-last (default auto)
          --[no-]bundle-licenses     Aggregate file headers/licenses at the top and add
                                     provenance banners between inlined sections (default on)
          --[no-]preserve-comments   Keep comments (default on)
          --minify                   Emit shortest equivalent text (drops comments)
          --dry-run                  Run the pipeline but write nothing
          --diff                     Print a unified diff of input vs bundled output
          --verbose                  List inlined files, renames, and normalizations
          -h, --help                 Show this help

        Exit codes: 0 success, 1 error-severity diagnostics, 2 usage error.

        """;
}
