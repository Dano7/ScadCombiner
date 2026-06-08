namespace ScadBundler.Core.Diagnostics;

/// <summary>The severity of a <see cref="Diagnostic"/>.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Invalid/ambiguous input; output is not produced (or only with <c>--force</c>).</summary>
    Error,

    /// <summary>Output IS produced, but something was changed or is risky.</summary>
    Warning,

    /// <summary>Purely informational; surfaced under <c>--verbose</c>.</summary>
    Info,
}
