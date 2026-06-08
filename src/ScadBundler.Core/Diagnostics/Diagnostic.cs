using ScadBundler.Core.Text;

namespace ScadBundler.Core.Diagnostics;

/// <summary>
/// A single diagnostic message produced by any pipeline phase. Every diagnostic carries a
/// <see cref="SourceSpan"/> and renders as
/// <c>&lt;severity&gt; &lt;code&gt;: &lt;message&gt;  (&lt;file&gt;:&lt;line&gt;:&lt;col&gt;)</c>.
/// </summary>
/// <param name="Code">The <c>SBnnnn</c> code, e.g. <c>"SB1001"</c>.</param>
/// <param name="Severity">The severity level.</param>
/// <param name="Message">The fully-rendered, human-facing message.</param>
/// <param name="Span">The source range the diagnostic refers to.</param>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan Span);
