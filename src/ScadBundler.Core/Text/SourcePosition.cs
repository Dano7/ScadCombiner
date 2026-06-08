namespace ScadBundler.Core.Text;

/// <summary>
/// A position in a source file.
/// </summary>
/// <param name="Offset">0-based char index into <see cref="SourceFile.Text"/>.</param>
/// <param name="Line">1-based line number (for human-facing diagnostics).</param>
/// <param name="Column">1-based column number (for human-facing diagnostics).</param>
public readonly record struct SourcePosition(int Offset, int Line, int Column);
