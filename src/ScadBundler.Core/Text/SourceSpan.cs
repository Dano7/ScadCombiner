namespace ScadBundler.Core.Text;

/// <summary>
/// A half-open span <c>[Start, End)</c> within a single file. A span never crosses files.
/// </summary>
/// <param name="File">The file this span belongs to.</param>
/// <param name="Start">Inclusive start position.</param>
/// <param name="End">Exclusive end position.</param>
public readonly record struct SourceSpan(SourceFile File, SourcePosition Start, SourcePosition End)
{
    /// <summary>
    /// Span for synthesized nodes that have no origin node to borrow from.
    /// </summary>
    public static readonly SourceSpan Synthetic =
        new(SourceFile.Synthesized, default, default);
}
