using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using ScadBundler.Core.Text;
using ScadBundler.Core.Transforming;
using Xunit;

namespace ScadBundler.Core.Tests.Transforming;

/// <summary>
/// Coverage for the transform infrastructure across <i>every</i> AST node kind (via
/// <see cref="RichScad"/>): the read-only walker, the rebuild-everything <c>TreeRewriter</c> base, the
/// rename rewriter, and the no-op <c>EmptyModel</c> default.
/// </summary>
public sealed class TransformInternalsTests
{
    [Fact]
    public void DescendantsAndSelf_VisitsAcrossAllNodeKinds()
    {
        ScadFile root = ParseHelper.Parse(RichScad.Source).Root;
        IReadOnlyList<AstNode> nodes = [.. AstNodes.DescendantsAndSelf(root)];

        // A spread of node kinds the walker must descend into.
        Assert.Contains(nodes, n => n is ForComprehension);
        Assert.Contains(nodes, n => n is ForCComprehension);
        Assert.Contains(nodes, n => n is IfComprehension);
        Assert.Contains(nodes, n => n is LetComprehension);
        Assert.Contains(nodes, n => n is EachExpression);
        Assert.Contains(nodes, n => n is FunctionLiteral);
        Assert.Contains(nodes, n => n is RangeExpression);
        Assert.Contains(nodes, n => n is MemberExpression);
        Assert.Contains(nodes, n => n is Binding);
        Assert.Contains(nodes, n => n is Parameter);
    }

    [Fact]
    public void MentionedNames_CollectsDeclarationAndReferenceNames()
    {
        HashSet<string> names = AstNodes.MentionedNames(ParseHelper.Parse(RichScad.Source).Root);
        Assert.Contains("a", names);     // assignment + references
        Assert.Contains("max", names);   // call callee
        Assert.Contains("$fn", names);   // special variable
        Assert.Contains("x", names);     // member access
    }

    [Fact]
    public void TreeRewriter_RebuildsEveryNodeKind_StructurePreserved()
    {
        ScadFile original = ParseHelper.Parse(RichScad.Source).Root;
        ScadFile rebuilt = new NoOpRewriter().Rewrite(original);
        Assert.Equal(StructuralKey.Of(original), StructuralKey.Of(rebuilt));
    }

    [Fact]
    public void NameRewriter_WithEmptyMap_IsStructuralIdentity()
    {
        ScadFile original = ParseHelper.Parse(RichScad.Source).Root;
        ScadFile rebuilt = new NameRewriter(
            new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance)).Rewrite(original);
        Assert.Equal(StructuralKey.Of(original), StructuralKey.Of(rebuilt));
    }

    [Fact]
    public void EmptyModel_ReturnsEmptyForEveryQuery()
    {
        EmptyModel model = EmptyModel.Instance;
        var file = new SourceFile("x.scad", string.Empty);
        var node = new Identifier("z");

        Assert.Empty(model.Modules(file));
        Assert.Empty(model.Functions(file));
        Assert.Empty(model.TopLevelVariables(file));
        Assert.Empty(model.PrivateConstants(file));
        Assert.Empty(model.PrivateConstants([file]));
        Assert.Null(model.Resolve(node));
        Assert.Empty(model.ReferencesTo(new Symbol(SymbolKind.Variable, "z", file, node)));
    }

    [Fact]
    public void Prologue_RecognizesEveryLiteralForm_StopsAtComputed()
    {
        ScadFile file = ParseHelper.Parse(
            "n = 5;\nv = [1, 2];\nu = -3;\nr = [0 : 2 : 10];\np = (1);\nc = sin(2);\ncube(1);\n").Root;

        HashSet<AstNode> nodes = Prologue.NodesOf(file);
        // n (number), v (vector), u (unary), r (range), p (paren) are literal forms; c (computed) ends it.
        Assert.Equal(5, nodes.Count);
    }

    [Fact]
    public void Prologue_StopsAtTheStickyHiddenFence()
    {
        ScadFile file = ParseHelper.Parse("a = 1;\nb = 2;\n").Root;
        var fence = new CommentTrivia("/* [Hidden] */", CommentKind.Block) { Span = SourceSpan.Synthetic, Sticky = true };
        Statement fenced = file.Statements[1] with { LeadingTrivia = [fence] };
        var bundle = file with { Statements = [file.Statements[0], fenced] };

        HashSet<AstNode> nodes = Prologue.NodesOf(bundle);
        Assert.Single(nodes);
        Assert.Contains(file.Statements[0], nodes);

        // A user's own identical-but-non-sticky comment is not the synthesized fence.
        Statement userComment = new EmptyStatement
        {
            LeadingTrivia = [new CommentTrivia("/* [Hidden] */", CommentKind.Block) { Span = SourceSpan.Synthetic }],
        };
        Assert.False(Prologue.HasHiddenFence(userComment));
    }

    [Fact]
    public void LiteralCanonicalization_LeavesNonFiniteNegativeAndAlreadyShortUntouched()
    {
        var bundle = new ScadFile(new SourceFile("x.scad", string.Empty),
        [
            Assign("a", new NumberLiteral(double.NaN, "nan")),
            Assign("b", new NumberLiteral(-5, "-5")),       // value < 0 guard
            Assign("c", new NumberLiteral(7, "7")),         // already shortest
        ]);

        ScadFile result = new LiteralCanonicalization().Apply(
            bundle, new TransformContext(HardeningProfile.Minify, 1UL, new DiagnosticBag()));

        List<AssignmentStatement> assignments = [.. result.Statements.OfType<AssignmentStatement>()];
        Assert.Equal("nan", ((NumberLiteral)assignments[0].Value).RawText);
        Assert.Equal("-5", ((NumberLiteral)assignments[1].Value).RawText);
        Assert.Equal("7", ((NumberLiteral)assignments[2].Value).RawText);
    }

    [Fact]
    public void StringDecomposition_SkipsNonAsciiAndPrologueStrings()
    {
        var context = new TransformContext(HardeningProfile.Obfuscate, 1UL, new DiagnosticBag());

        // Non-prologue strings (after a statement): the ASCII one decomposes, the non-ASCII one does not.
        ScadFile body = ParseHelper.Parse("cube(1);\nx = \"café\";\ny = \"ok\";\n").Root;
        string bodyText = Emitter.Emit(new StringDecomposition().Apply(body, context), EmitOptions.Default);
        Assert.Contains("\"café\"", bodyText, StringComparison.Ordinal); // non-ASCII left literal
        Assert.Contains("chr(", bodyText, StringComparison.Ordinal);          // ASCII decomposed

        // A leading literal string is a Customizer parameter — never decomposed (must stay literal).
        ScadFile prologue = ParseHelper.Parse("title = \"hi\";\ncube(1);\n").Root;
        string prologueText = Emitter.Emit(new StringDecomposition().Apply(prologue, context), EmitOptions.Default);
        Assert.Contains("title = \"hi\"", prologueText, StringComparison.Ordinal);
        Assert.DoesNotContain("chr(", prologueText, StringComparison.Ordinal);
    }

    private static AssignmentStatement Assign(string name, Expression value) =>
        new(name, value) { Span = SourceSpan.Synthetic };

    // A TreeRewriter that changes nothing — exercises every rebuild arm with an identity Transform.
    private sealed class NoOpRewriter : TreeRewriter
    {
    }
}
