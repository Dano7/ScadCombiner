using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Core.Inlining;
using ScadBundler.Web.Components;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit tests for the W3 <see cref="OptionsPanel"/>: each control maps to the right
/// <see cref="WebBundleOptions"/> field via <see cref="WorkspaceController.SetOptions"/>, the profile radio
/// is mutually exclusive, "strip license" enables only under a hardening profile, "keep comments" disables
/// under minify, and any change re-bundles. All wired to a real <see cref="WorkspaceController"/> so the
/// mapping matches the live app (the byte-level CLI parity is proven separately in <c>BundleParityTests</c>).
/// </summary>
public sealed class OptionsPanelTests : TestContext
{
    // Checkbox document order: [0] remove-provenance, [1] strip-license, [2] keep-comments.
    private const int Provenance = 0;
    private const int StripLicense = 1;
    private const int KeepComments = 2;

    // Radio document order: [0] Normal, [1] Minify, [2] Obfuscate.
    private const int Normal = 0;
    private const int Minify = 1;
    private const int Obfuscate = 2;

    private (WorkspaceController Controller, IRenderedComponent<OptionsPanel> Cut) Render()
    {
        var controller = new WorkspaceController();
        // A complete project so a bundle exists and option changes re-bundle.
        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "// MIT License\ninclude <lib.scad>\nwidget(3);\n"),
            new UploadedFile("lib.scad", "module widget(w) cube(w);\n"),
        ]);
        Services.AddSingleton(controller);
        IRenderedComponent<OptionsPanel> cut = RenderComponent<OptionsPanel>();
        return (controller, cut);
    }

    private static IElement Checkbox(IRenderedComponent<OptionsPanel> cut, int index) =>
        cut.FindAll("input[type=checkbox]")[index];

    private static IElement Radio(IRenderedComponent<OptionsPanel> cut, int index) =>
        cut.FindAll("input[type=radio]")[index];

    [Fact]
    public void ProvenanceCheckbox_TogglesBundleLicenses_Inverted()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();
        Assert.True(controller.Options.BundleLicenses);          // default: keep attribution

        Checkbox(cut, Provenance).Change(true);                  // "remove provenance" ticked

        Assert.False(controller.Options.BundleLicenses);
    }

    [Fact]
    public void ProfileRadio_SetsHardening_AndIsMutuallyExclusive()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();
        Assert.Equal(HardeningProfile.None, controller.Options.Hardening);

        Radio(cut, Minify).Change(true);
        Assert.Equal(HardeningProfile.Minify, controller.Options.Hardening);

        Radio(cut, Obfuscate).Change(true);
        Assert.Equal(HardeningProfile.Obfuscate, controller.Options.Hardening);   // not "both" — single enum

        Radio(cut, Normal).Change(true);
        Assert.Equal(HardeningProfile.None, controller.Options.Hardening);
    }

    [Fact]
    public void StripLicense_EnabledOnlyUnderAHardeningProfile()
    {
        (_, IRenderedComponent<OptionsPanel> cut) = Render();
        Assert.True(Checkbox(cut, StripLicense).HasAttribute("disabled"));        // Normal ⇒ disabled

        Radio(cut, Minify).Change(true);

        Assert.False(Checkbox(cut, StripLicense).HasAttribute("disabled"));       // profile ⇒ enabled
    }

    [Fact]
    public void StripLicense_TogglesOption_UnderProfile()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();
        Radio(cut, Obfuscate).Change(true);

        Checkbox(cut, StripLicense).Change(true);

        Assert.True(controller.Options.StripLicense);
        Assert.Equal(HardeningProfile.Obfuscate, controller.Options.Hardening);
    }

    [Fact]
    public void KeepComments_OnByDefault_AndDisabledUnderMinify()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();
        Assert.True(controller.Options.PreserveComments);
        Assert.False(Checkbox(cut, KeepComments).HasAttribute("disabled"));       // Normal ⇒ editable

        Radio(cut, Minify).Change(true);

        Assert.True(Checkbox(cut, KeepComments).HasAttribute("disabled"));        // minify drops comments
    }

    [Fact]
    public void KeepComments_TogglesPreserveComments()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();

        Checkbox(cut, KeepComments).Change(false);

        Assert.False(controller.Options.PreserveComments);
    }

    [Fact]
    public void CollisionSelect_UpdatesStrategy()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();
        Assert.Equal(CollisionStrategy.Auto, controller.Options.OnCollision);

        cut.Find("select").Change(nameof(CollisionStrategy.Prefix));

        Assert.Equal(CollisionStrategy.Prefix, controller.Options.OnCollision);
    }

    [Fact]
    public void ChangingAnOption_ReBundlesLive()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = Render();
        string before = controller.Bundle!.Text;
        int changes = 0;
        controller.Changed += () => changes++;

        Radio(cut, Minify).Change(true);                         // minify ⇒ a different (smaller) bundle

        Assert.Equal(1, changes);                                // SetOptions → Recompute → Changed
        Assert.NotNull(controller.Bundle);
        Assert.NotEqual(before, controller.Bundle!.Text);
    }
}
