using ScadBundler.Core.Text;

namespace ScadBundler.Core.Diagnostics;

/// <summary>
/// A collecting sink for <see cref="Diagnostic"/>s. Pipeline phases report into a bag rather
/// than throwing, so a malformed input still yields a (best-effort) result alongside its errors.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    /// <summary>The number of diagnostics collected so far.</summary>
    public int Count => _items.Count;

    /// <summary>Records a diagnostic.</summary>
    public void Report(string code, DiagnosticSeverity severity, string message, SourceSpan span) =>
        _items.Add(new Diagnostic(code, severity, message, span));

    /// <summary>Records an <see cref="DiagnosticSeverity.Error"/>.</summary>
    public void Error(string code, string message, SourceSpan span) =>
        Report(code, DiagnosticSeverity.Error, message, span);

    /// <summary>Records a <see cref="DiagnosticSeverity.Warning"/>.</summary>
    public void Warning(string code, string message, SourceSpan span) =>
        Report(code, DiagnosticSeverity.Warning, message, span);

    /// <summary>Records an <see cref="DiagnosticSeverity.Info"/>.</summary>
    public void Info(string code, string message, SourceSpan span) =>
        Report(code, DiagnosticSeverity.Info, message, span);

    /// <summary>Returns the collected diagnostics as an immutable snapshot.</summary>
    public IReadOnlyList<Diagnostic> ToList() => _items.ToArray();
}
