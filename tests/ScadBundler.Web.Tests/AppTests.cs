using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Web;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// Renders the full <see cref="App"/> (not a component in isolation) to guard the Razor markup wiring — the
/// component tests set parameters via the typed builder API, so a string-typed attribute missing its <c>@</c>
/// (a literal instead of an expression) slips past them. Regression for the <c>RootText="Controller.RootText"</c>
/// bug, which showed the binding text in the editor and produced a wrong download name.
/// </summary>
public sealed class AppTests : TestContext
{
    [Fact]
    public void MainFileEditor_AndDownloadName_BindToValues_NotLiteralExpressions()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;          // DropZone's OnAfterRender calls JS; loose = no-op
        var controller = new WorkspaceController();
        controller.AddOrReplace([new UploadedFile("main.scad", "// my model\ncube(7);\n")]);
        Services.AddSingleton(controller);

        IRenderedComponent<App> cut = RenderComponent<App>();

        // The editor textarea shows the actual root-file contents (whether rendered as the value attribute
        // or inner text), never the literal "Controller.RootText".
        IElement editor = cut.Find("textarea.editor-text");
        string shown = (editor.GetAttribute("value") ?? string.Empty) + editor.TextContent;
        Assert.Contains("cube(7)", shown);
        Assert.DoesNotContain("Controller.RootText", cut.Markup);
        Assert.DoesNotContain("Editing <code>Controller.Root", cut.Markup);

        // OutputPanel.RootPath is also string-typed: the download name derives from the real root stem.
        Assert.Contains("main.bundled.scad", cut.Markup);
        Assert.DoesNotContain("Controller.bundled.scad", cut.Markup);
    }
}
