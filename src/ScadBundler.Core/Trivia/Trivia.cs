using ScadBundler.Core.Text;

// NOTE: These types live in the root ScadBundler.Core namespace rather than
// ScadBundler.Core.Trivia, because a namespace named "Trivia" would clash with
// the "Trivia" type when referenced from sibling namespaces (CS0118). The folder
// layout still groups them under Trivia/ per the AST reference.
namespace ScadBundler.Core;

/// <summary>
/// Non-semantic source text (comments) that the parser attaches to the nearest node so the
/// emitter can reproduce it. Trivia is <b>not</b> visited as part of the main tree walk.
/// Blank lines are not trivia — they are captured by a <c>BlankLineBefore</c> flag.
/// </summary>
public abstract record Trivia
{
    /// <summary>The source range this trivia covers.</summary>
    public required SourceSpan Span { get; init; }
}

/// <summary>
/// A comment. <see cref="Text"/> is the full raw comment <b>including</b> delimiters
/// (<c>// ...</c> or <c>/* ... */</c>), so it can be re-emitted verbatim.
/// </summary>
/// <param name="Text">The raw comment text, including delimiters.</param>
/// <param name="Kind">Whether the comment is a line or block comment.</param>
public sealed record CommentTrivia(string Text, CommentKind Kind) : Trivia
{
    /// <summary>
    /// When <c>true</c>, this comment survives the comment-stripping emit modes
    /// (<c>--minify</c> / <c>--no-preserve-comments</c> / <c>--obfuscate</c>) instead of being dropped.
    /// Set on the aggregated license header, the synthesized Customizer <c>/* [Hidden] */</c> fence, and
    /// the comments OpenSCAD's Customizer reads off each hoisted parameter (its <c>/* [group] */</c>
    /// header, its description line, and its trailing <c>// [..]</c> annotation) so a hardened
    /// (minified/obfuscated) bundle keeps its legal header and the same Customizer grouping and labels as
    /// the original. Ignored when comments are preserved (every comment is emitted then). Default <c>false</c>.
    /// </summary>
    public bool Sticky { get; init; }
}

/// <summary>The lexical form of a comment.</summary>
public enum CommentKind
{
    /// <summary><c>// ...</c> to end of line.</summary>
    Line,

    /// <summary><c>/* ... */</c>, possibly spanning multiple lines.</summary>
    Block,
}
