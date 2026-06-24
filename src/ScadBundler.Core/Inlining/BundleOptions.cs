namespace ScadBundler.Core.Inlining;

/// <summary>
/// How the inliner resolves a name collision across merged files. <see cref="Auto"/> is the
/// correctness-preserving default (origin-dependent: <c>include</c> duplicates keep-last, <c>use</c>
/// imports are namespaced); the rest force one strategy everywhere.
/// </summary>
public enum CollisionStrategy
{
    /// <summary>Origin-dependent: <c>include</c> duplicates keep-last (last-wins); <c>use</c>-imported
    /// names are namespaced on collision. Preserves OpenSCAD semantics and library isolation.</summary>
    Auto,

    /// <summary>Namespace every colliding definition (both origins).</summary>
    Prefix,

    /// <summary>Any collision is an error; no output is produced.</summary>
    Error,

    /// <summary>Keep the first definition of each colliding name (bundle emit order); silently drop
    /// the rest. The one strategy that deliberately diverges from OpenSCAD's native last-wins: a
    /// bundle-time repair for when a later — often transitively included, third-party — file stomps a
    /// name the model was written against and the sources can't be edited or reordered. A reassigned
    /// variable keeps its first assignment's original expression, and the output is intentionally not
    /// equivalent to the original project (see UX.md "Collision Strategies").</summary>
    KeepFirst,

    /// <summary>Keep the last definition of each colliding name; drop the rest.</summary>
    KeepLast,
}

/// <summary>
/// A post-inline output-hardening profile (Slice 7). Two mutually exclusive profiles share one
/// transform engine over the flattened bundle, governed by one correctness bar (byte-identical CSG).
/// </summary>
public enum HardeningProfile
{
    /// <summary>No hardening — emit the bundle as inlined.</summary>
    None,

    /// <summary>Minimize byte size: tree-shake dead definitions, shorten identifiers, canonicalize
    /// literals. Incidentally unreadable.</summary>
    Minify,

    /// <summary>Maximize the cost of reverse-engineering: opaque identifiers, indirection, render-inert
    /// decoys, decomposed strings. Output may be larger than the input.</summary>
    Obfuscate,
}

/// <summary>
/// Options controlling a bundle. <see cref="LibraryPaths"/> are the extra search-path entries
/// (<c>-p</c> followed by <c>OPENSCADPATH</c>, then library dirs) consulted after the including file's
/// own directory.
/// </summary>
/// <param name="LibraryPaths">Extra search paths, in priority order, after the including file's directory.</param>
/// <param name="OnCollision">The collision-resolution strategy.</param>
/// <param name="BundleLicenses">When <c>true</c> (the default), every bundled file's leading
/// header/license comments are aggregated and deduplicated at the top of the bundle, and one-line
/// provenance banners separate the inlined sections (the attribution pass; SB5007).</param>
/// <param name="PreserveComments">When <c>true</c>, comment trivia is preserved through bundling.</param>
/// <param name="Hardening">The post-inline hardening profile (Slice 7); <see cref="HardeningProfile.None"/>
/// emits the bundle unchanged.</param>
/// <param name="StripLicense">When <c>true</c>, the aggregated license header is <b>not</b> kept through a
/// hardening profile (it is emitted in normal mode but dropped under <c>--minify</c>/<c>--obfuscate</c>
/// like any other comment). Default <c>false</c> — legal text survives a hardened bundle.</param>
/// <param name="Lint">When <c>true</c>, surface the static source-lint diagnostics the bundler can derive
/// but OpenSCAD does <b>not</b> report at parse time — unknown references (SB3005) and module/function
/// redefinitions (SB3004). Off by default so the bundle stays silent wherever OpenSCAD is silent: those
/// checks are static approximations of OpenSCAD's <i>evaluation-time</i> behavior (unknown reads yield
/// <c>undef</c>; redefinitions silently last-win), so they false-positive on dead code, short-circuited
/// reads, optional config variables, and intra-library duplicates in real-world libraries (e.g. BOSL2).
/// The collision is still resolved either way; <c>--lint</c> only controls whether it is reported.</param>
/// <param name="ParametersFirst">When <c>true</c>, emit the aggregated license/header block <b>below</b>
/// the Customizer parameter prologue (above the body) instead of above the parameters, so the parameters
/// lead the file. An opt-in platform-compatibility workaround: Thingiverse's Customizer fails to surface
/// parameters that follow a long leading comment block; promoting them above the header restores them.
/// Comment-relocation only (the prologue is already hoisted and protected) — the rendered CSG is
/// unchanged, and the license still appears, just lower. A no-op without an aggregated header to move
/// (e.g. <c>--no-bundle-licenses</c>) or without a Customizer prologue. See
/// <see href="../../docs/adr/0002-parameters-first-customizer-hoist.md">ADR 0002</see>.</param>
public sealed record BundleOptions(
    IReadOnlyList<string> LibraryPaths,
    CollisionStrategy OnCollision = CollisionStrategy.Auto,
    bool BundleLicenses = true,
    bool PreserveComments = true,
    HardeningProfile Hardening = HardeningProfile.None,
    bool StripLicense = false,
    bool Lint = false,
    bool ParametersFirst = false)
{
    /// <summary>Default options: no extra library paths, <see cref="CollisionStrategy.Auto"/>.</summary>
    public static BundleOptions Default { get; } = new([]);
}
