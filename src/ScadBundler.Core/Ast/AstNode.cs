using ScadBundler.Core.Text;

namespace ScadBundler.Core.Ast;

/// <summary>
/// Base of every node in the ScadBundler abstract syntax tree. Nodes are immutable records;
/// transformations produce new trees via <c>with</c> expressions. Every node carries its
/// <see cref="SourceSpan"/> plus attached comment trivia and a blank-line marker so the emitter
/// can faithfully round-trip author intent.
/// </summary>
public abstract record AstNode
{
    /// <summary>
    /// The source range this node covers. Defaults to <see cref="SourceSpan.Synthetic"/> so nodes
    /// can be built and then have their span attached via a <c>with</c> expression; the parser
    /// always assigns a real span. Never null (the default carries the synthesized sentinel file).
    /// </summary>
    public SourceSpan Span { get; init; } = SourceSpan.Synthetic;

    /// <summary>
    /// Comments attached before this node (e.g. a Customizer label line, a license header, a
    /// section banner). Empty when none. Sourced from the node's first token.
    /// </summary>
    public IReadOnlyList<Trivia> LeadingTrivia { get; init; } = [];

    /// <summary>
    /// Comments attached after this node on the same line (e.g. a Customizer inline annotation
    /// <c>// [0:100]</c>). Empty when none. Sourced from the node's last token.
    /// </summary>
    public IReadOnlyList<Trivia> TrailingTrivia { get; init; } = [];

    /// <summary>
    /// True when one or more blank lines preceded this node in the source. The emitter renders
    /// exactly one blank line before the node when set (honored at statement boundaries).
    /// </summary>
    public bool BlankLineBefore { get; init; }

    /// <summary>Dispatches to the matching <see cref="IAstVisitor{TResult}.Visit(ScadFile)"/> overload.</summary>
    /// <typeparam name="TResult">The visitor's result type.</typeparam>
    /// <param name="visitor">The visitor to dispatch to.</param>
    /// <returns>The visitor's result for this node.</returns>
    public abstract TResult Accept<TResult>(IAstVisitor<TResult> visitor);
}
