using ScadBundler.Core.Ast;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// The dedup content key: it must be deterministic, ignore span/trivia/blank-line, and distinguish
/// genuinely different shapes. Calling <see cref="StructuralKey.Of"/> on a whole-tree fixture also
/// exercises the visitor across every node kind.
/// </summary>
public sealed class StructuralKeyTests
{
    [Fact]
    public void Of_WholeTree_IsStableAndNonEmpty()
    {
        ScadFile file = SemanticHelper.ParseFile(RichScad.Source);

        string first = StructuralKey.Of(file);
        string second = StructuralKey.Of(file);

        Assert.NotEqual(string.Empty, first);
        Assert.Equal(first, second); // deterministic
    }

    [Fact]
    public void Of_IgnoresSpanTriviaAndBlankLine()
    {
        // Same shape, different formatting/blank lines → identical key.
        ScadFile compact = SemanticHelper.ParseFile("module box() cube(1);");
        ScadFile spaced = SemanticHelper.ParseFile("\n\n// banner\nmodule box()   cube( 1 );\n");

        Assert.Equal(
            StructuralKey.Of(compact.Statements[0]),
            StructuralKey.Of(spaced.Statements[0]));
    }

    [Fact]
    public void Of_DistinguishesDifferentBodies()
    {
        ScadFile cube = SemanticHelper.ParseFile("module box() cube(1);");
        ScadFile sphere = SemanticHelper.ParseFile("module box() sphere(1);");

        Assert.NotEqual(
            StructuralKey.Of(cube.Statements[0]),
            StructuralKey.Of(sphere.Statements[0]));
    }

    [Fact]
    public void Of_DistinguishesNumberRawText()
    {
        // Same numeric value, different lexical form (1 vs 1.0) → different key (round-trip fidelity).
        ScadFile plain = SemanticHelper.ParseFile("x = 1;");
        ScadFile dotted = SemanticHelper.ParseFile("x = 1.0;");

        Assert.NotEqual(
            StructuralKey.Of(plain.Statements[0]),
            StructuralKey.Of(dotted.Statements[0]));
    }
}
