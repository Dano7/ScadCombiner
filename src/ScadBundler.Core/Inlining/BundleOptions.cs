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
public sealed record BundleOptions(
    IReadOnlyList<string> LibraryPaths,
    CollisionStrategy OnCollision = CollisionStrategy.Auto,
    bool BundleLicenses = true,
    bool PreserveComments = true)
{
    /// <summary>Default options: no extra library paths, <see cref="CollisionStrategy.Auto"/>.</summary>
    public static BundleOptions Default { get; } = new([]);
}
