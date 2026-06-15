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
/// The option value is set synchronously by <c>SetOptions</c>; the re-bundle is async (Slice W5 §C1), so
/// tests that assert the re-bundled output <c>await</c> <see cref="WorkspaceController.Recomputing"/>.
/// </summary>
public sealed class OptionsPanelTests : TestContext
{
    // Generous window for WaitForAssertion (used by the back-to-back-change tests): this suite can run beside
    // the CPU-heavy integration suite (the W2 MainFileEditor timing note).
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    // Checkbox document order: [0] remove-provenance, [1] strip-license, [2] keep-comments.
    private const int Provenance = 0;
    private const int StripLicense = 1;
    private const int KeepComments = 2;

    // Radio document order: [0] Normal, [1] Minify, [2] Obfuscate.
    private const int Normal = 0;
    private const int Minify = 1;
    private const int Obfuscate = 2;

    private async Task<(WorkspaceController Controller, IRenderedComponent<OptionsPanel> Cut)> RenderAsync()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        // A complete project so a bundle exists and option changes re-bundle.
        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "// MIT License\ninclude <lib.scad>\nwidget(3);\n"),
            new UploadedFile("lib.scad", "module widget(w) cube(w);\n"),
        ]);
        await controller.Recomputing;
        Services.AddSingleton(controller);
        IRenderedComponent<OptionsPanel> cut = RenderComponent<OptionsPanel>();
        return (controller, cut);
    }

    private static IElement Checkbox(IRenderedComponent<OptionsPanel> cut, int index) =>
        cut.FindAll("input[type=checkbox]")[index];

    private static IElement Radio(IRenderedComponent<OptionsPanel> cut, int index) =>
        cut.FindAll("input[type=radio]")[index];

    [Fact]
    public async Task ProvenanceCheckbox_TogglesBundleLicenses_Inverted()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        Assert.True(controller.Options.BundleLicenses);          // default: keep attribution

        Checkbox(cut, Provenance).Change(true);                  // "remove provenance" ticked
        await controller.Recomputing;

        Assert.False(controller.Options.BundleLicenses);
    }

    [Fact]
    public async Task ProfileRadio_SetsHardening_AndIsMutuallyExclusive()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        Assert.Equal(HardeningProfile.None, controller.Options.Hardening);

        // A UI event raised while a recompute is in flight is *queued* on the dispatcher (Slice W5 §C1), so a
        // back-to-back change may not have committed synchronously. WaitForAssertion pumps the renderer until
        // it does — reliable here because Options is set in the change handler, which re-renders the panel.
        Radio(cut, Minify).Change(true);
        cut.WaitForAssertion(() => Assert.Equal(HardeningProfile.Minify, controller.Options.Hardening), Settle);

        Radio(cut, Obfuscate).Change(true);                                       // not "both" — single enum
        cut.WaitForAssertion(() => Assert.Equal(HardeningProfile.Obfuscate, controller.Options.Hardening), Settle);

        Radio(cut, Normal).Change(true);
        cut.WaitForAssertion(() => Assert.Equal(HardeningProfile.None, controller.Options.Hardening), Settle);
    }

    [Fact]
    public async Task StripLicense_EnabledOnlyUnderAHardeningProfile()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        Assert.True(Checkbox(cut, StripLicense).HasAttribute("disabled"));        // Normal ⇒ disabled

        Radio(cut, Minify).Change(true);
        await controller.Recomputing;

        Assert.False(Checkbox(cut, StripLicense).HasAttribute("disabled"));       // profile ⇒ enabled
    }

    [Fact]
    public async Task StripLicense_TogglesOption_UnderProfile()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        Radio(cut, Obfuscate).Change(true);
        // The strip-license checkbox enables once a profile is picked; wait for that re-render before ticking.
        cut.WaitForAssertion(() => Assert.False(Checkbox(cut, StripLicense).HasAttribute("disabled")), Settle);

        Checkbox(cut, StripLicense).Change(true);

        cut.WaitForAssertion(() => Assert.True(controller.Options.StripLicense), Settle);
        Assert.Equal(HardeningProfile.Obfuscate, controller.Options.Hardening);
    }

    [Fact]
    public async Task KeepComments_OnByDefault_AndDisabledUnderMinify()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        Assert.True(controller.Options.PreserveComments);
        Assert.False(Checkbox(cut, KeepComments).HasAttribute("disabled"));       // Normal ⇒ editable

        Radio(cut, Minify).Change(true);
        await controller.Recomputing;

        Assert.True(Checkbox(cut, KeepComments).HasAttribute("disabled"));        // minify drops comments
    }

    [Fact]
    public async Task KeepComments_TogglesPreserveComments()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();

        Checkbox(cut, KeepComments).Change(false);
        await controller.Recomputing;

        Assert.False(controller.Options.PreserveComments);
    }

    [Fact]
    public async Task CollisionSelect_UpdatesStrategy()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        Assert.Equal(CollisionStrategy.Auto, controller.Options.OnCollision);

        cut.Find("select").Change(nameof(CollisionStrategy.Prefix));
        await controller.Recomputing;

        Assert.Equal(CollisionStrategy.Prefix, controller.Options.OnCollision);
    }

    [Fact]
    public async Task ChangingAnOption_ReBundlesLive()
    {
        (WorkspaceController controller, IRenderedComponent<OptionsPanel> cut) = await RenderAsync();
        string before = controller.Bundle!.Text;
        int changes = 0;
        controller.Changed += () => changes++;

        Radio(cut, Minify).Change(true);                         // minify ⇒ a different (smaller) bundle
        await controller.Recomputing;

        Assert.True(changes > 0);                                // SetOptions → recompute → Changed (per phase)
        Assert.NotNull(controller.Bundle);
        Assert.NotEqual(before, controller.Bundle!.Text);
    }
}
