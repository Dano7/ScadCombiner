namespace ScadBundler.Core.Text;

/// <summary>
/// A loaded source file. One instance per physical file read.
/// </summary>
/// <param name="Path">The path the file was loaded from (display/diagnostic use).</param>
/// <param name="Text">The full source text, already decoded to a .NET (UTF-16) string.</param>
public sealed record SourceFile(string Path, string Text)
{
    /// <summary>
    /// Sentinel file for nodes created by transforms with no real origin
    /// (e.g. a bundler-generated header). <see cref="Path"/> = "&lt;synthesized&gt;", <see cref="Text"/> = "".
    /// </summary>
    public static readonly SourceFile Synthesized = new("<synthesized>", "");
}
