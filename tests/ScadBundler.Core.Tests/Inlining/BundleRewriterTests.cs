using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// The single bundle rewrite pass: with no renames and no deprecated constructs it must be a faithful
/// identity over every node kind (exercised via the whole-tree fixture); renames substitute the bound
/// name on reference and declaration nodes keyed by identity.
/// </summary>
public sealed class BundleRewriterTests
{
    [Fact]
    public void Rewrite_NoRenames_IsStructuralIdentity_AcrossAllNodeKinds()
    {
        ScadFile file = SemanticHelper.ParseFile(RichScad.Source);
        var rewriter = new BundleRewriter(
            new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance), new DiagnosticBag());

        foreach (Statement statement in file.Statements)
        {
            Statement rewritten = rewriter.RewriteStatement(statement);
            Assert.Equal(StructuralKey.Of(statement), StructuralKey.Of(rewritten));
        }
    }

    [Fact]
    public void Rewrite_AppliesRename_ToBoundIdentifier()
    {
        ScadFile file = SemanticHelper.ParseFile("x = y + 1;");
        var read = SemanticHelper.Find<Identifier>(file, i => i.Name == "y");
        var renames = new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance) { [read] = "z" };
        var rewriter = new BundleRewriter(renames, new DiagnosticBag());

        var rewritten = (AssignmentStatement)rewriter.RewriteStatement(file.Statements[0]);

        var binary = Assert.IsType<BinaryExpression>(rewritten.Value);
        Assert.Equal("z", Assert.IsType<Identifier>(binary.Left).Name);
    }

    [Fact]
    public void Rewrite_PreservesSpanAndTrivia_OnRenamedNode()
    {
        ScadFile file = SemanticHelper.ParseFile("module box() cube(1);");
        var definition = (ModuleDefinition)file.Statements[0];
        var renames = new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance) { [definition] = "box_renamed" };
        var rewriter = new BundleRewriter(renames, new DiagnosticBag());

        var rewritten = (ModuleDefinition)rewriter.RewriteStatement(definition);

        Assert.Equal("box_renamed", rewritten.Name);
        Assert.Equal(definition.Span, rewritten.Span); // origin span reused (provenance rule)
    }
}
