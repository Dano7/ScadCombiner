using System.Text;
using Bunit;
using ScadBundler.Web.Components;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for <see cref="OutputPanel"/>: Copy/Download are live <b>iff</b> the bundle is
/// <c>Ok</c> and non-empty (Slice W1 §5), the download name follows the CLI's
/// <c>&lt;rootstem&gt;.bundled.scad</c> shape, and nothing renders before a bundle exists.
/// </summary>
public sealed class OutputPanelTests : TestContext
{
    private static WebBundleResult OkResult(string text) =>
        new(text, true, [], new BundleStats(0, Encoding.UTF8.GetByteCount(text), 0, 0, 0));

    [Fact]
    public void EnablesButtons_AndNamesDownload_WhenOkAndNonEmpty()
    {
        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p
            .Add(c => c.Result, OkResult("cube(1);\n"))
            .Add(c => c.RootPath, "/proj/ForkedHolder.scad"));

        Assert.All(cut.FindAll("button"), b => Assert.False(b.HasAttribute("disabled")));
        Assert.Contains("ForkedHolder.bundled.scad", cut.Markup);
    }

    [Fact]
    public void ShowsErrorNotice_AndNoEmitButtons_WhenNotOk()
    {
        var blocked = new WebBundleResult(string.Empty, false, [], new BundleStats(0, 0, 0, 0, 0));

        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p.Add(c => c.Result, blocked));

        Assert.Empty(cut.FindAll("button"));                     // no Copy/Download offered
        Assert.Contains("role=\"alert\"", cut.Markup);
        Assert.Contains("must be fixed", cut.Markup);            // explains the error state
    }

    [Fact]
    public void RendersNoButtonsOrError_WhenOkButEmpty()
    {
        // Degenerate (Ok=true with no text never occurs in the real pipeline): show neither emit controls
        // nor the error notice — the error alert is reserved for a genuine !Ok bundle.
        var empty = new WebBundleResult(string.Empty, true, [], new BundleStats(0, 0, 0, 0, 0));

        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p.Add(c => c.Result, empty));

        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("must be fixed", cut.Markup);
    }

    [Fact]
    public void StatsLine_SurfacesRenamedAndRemoved_WhenProfileRan()
    {
        var hardened = new WebBundleResult(
            "x();\n", true, [], new BundleStats(FilesInlined: 1, OutputBytes: 5, Renames: 2, DefinitionsRemoved: 3, Normalizations: 0));

        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p.Add(c => c.Result, hardened));

        Assert.Contains("2 file", cut.Markup);                   // 1 inlined + root
        Assert.Contains("2 renamed", cut.Markup);
        Assert.Contains("3 removed", cut.Markup);
    }

    [Fact]
    public void RendersNothing_WhenResultNull()
    {
        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>();

        Assert.Empty(cut.Markup.Trim());
    }
}
