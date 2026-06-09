namespace ScadBundler.Core.Emitting;

/// <summary>How the emitter renders one level of indentation.</summary>
public enum IndentStyle
{
    /// <summary>Indent with <see cref="EmitOptions.IndentWidth"/> spaces per level.</summary>
    Spaces,

    /// <summary>Indent with one tab character per level (<see cref="EmitOptions.IndentWidth"/> is ignored).</summary>
    Tabs,
}

/// <summary>Where a block's opening brace is placed relative to its header.</summary>
public enum BraceStyle
{
    /// <summary>K&amp;R: <c>{</c> follows the header on the same line after one space.</summary>
    SameLine,

    /// <summary>Allman: <c>{</c> begins on the next line at the header's indent.</summary>
    NextLine,
}

/// <summary>
/// Configuration for the <see cref="Emitter"/>. The defaults (4-space indent, same-line braces,
/// comments preserved) are what lock the checked-in golden outputs; every setting is otherwise
/// adjustable. <see cref="MaxLineLength"/> is advisory in v1 (no hard wrapping) and reserved for a
/// future wrapping pass.
/// </summary>
/// <param name="IndentWidth">Spaces per indent level when <see cref="IndentStyle"/> is <see cref="IndentStyle.Spaces"/>.</param>
/// <param name="IndentStyle">Whether to indent with spaces or tabs.</param>
/// <param name="BraceStyle">Where block opening braces are placed.</param>
/// <param name="MaxLineLength">Advisory maximum line length (no hard wrapping in v1).</param>
/// <param name="Minify">When <c>true</c>, drop all comments, blank lines, and optional whitespace.</param>
/// <param name="PreserveComments">When <c>true</c>, comment trivia is emitted; ignored when <see cref="Minify"/> is set.</param>
public sealed record EmitOptions(
    int IndentWidth = 4,
    IndentStyle IndentStyle = IndentStyle.Spaces,
    BraceStyle BraceStyle = BraceStyle.SameLine,
    int MaxLineLength = 100,
    bool Minify = false,
    bool PreserveComments = true)
{
    /// <summary>The default options (4-space indent, same-line braces, comments preserved).</summary>
    public static readonly EmitOptions Default = new();
}
