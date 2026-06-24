using ScadBundler.Core.Inlining;

namespace ScadBundler.Core.Workspace;

/// <summary>
/// The browser-facing bundle options. Every field maps to an existing CLI flag / <see cref="BundleOptions"/>
/// field — the web app introduces no new behavior. <see cref="WebBundler"/> translates this to
/// <see cref="BundleOptions"/> + <see cref="Emitting.EmitOptions"/> exactly as the CLI's
/// <c>BundleCommand</c> does, so a Live bundle equals the CLI's output for the same inputs.
/// </summary>
/// <param name="BundleLicenses">When <c>true</c> (default), aggregate file headers/licenses at the top and
/// add provenance banners between inlined sections (<c>--[no-]bundle-licenses</c>).</param>
/// <param name="Hardening">The post-inline hardening profile (<c>--minify</c>/<c>--obfuscate</c>);
/// mutually exclusive by construction.</param>
/// <param name="StripLicense">When <c>true</c>, drop the aggregated license header under a hardening
/// profile (<c>--strip-license</c>); only meaningful with <see cref="Hardening"/> set.</param>
/// <param name="OnCollision">The collision-resolution strategy (<c>--on-collision</c>).</param>
/// <param name="PreserveComments">When <c>true</c> (default), keep comments (<c>--[no-]preserve-comments</c>);
/// ignored under minify (comments are already dropped).</param>
/// <param name="ParametersFirst">When <c>true</c>, emit Customizer parameters above the aggregated license
/// header so they lead the file (<c>--parameters-first</c>); an opt-in Thingiverse-Customizer compatibility
/// workaround. Default <c>false</c>.</param>
public sealed record WebBundleOptions(
    bool BundleLicenses = true,
    HardeningProfile Hardening = HardeningProfile.None,
    bool StripLicense = false,
    CollisionStrategy OnCollision = CollisionStrategy.Auto,
    bool PreserveComments = true,
    bool ParametersFirst = false);
