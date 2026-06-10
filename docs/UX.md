# ScadBundler User Experience Design

**Target Users**: OpenSCAD power users maintaining multi-file libraries (BOSL2, NopSCADlib, dotSCAD, custom projects) who need Thingiverse/MakerWorld/Customizer compatibility.

## Core Command-Line Interface
```bash
scadbundler bundle <input.scad> [options]
```

**Primary Options**:
- `-o, --output <file>`: Output bundled file (default: input.bundled.scad)
- `-p, --library-path <paths>`: Additional search paths (comma-separated or multiple flags). Respects OPENSCADPATH env var.
- `--on-collision <strategy>`: `auto`|`prefix`|`error`|`keep-first`|`keep-last` (default: **`auto`** = origin-dependent — `keep-last` for `include` collisions to match OpenSCAD's native last-wins, `prefix` for `use`d-library collisions to preserve library isolation; see [Spec.md](Spec.md) "Collision-strategy implication"). Any value other than `auto` forces that one strategy everywhere. See **Collision Strategies** below for what each does and when to reach for it.
- `--preserve-comments`: Keep all comments (default: true)
- `--[no-]bundle-licenses`: Aggregate every bundled file's leading header/license comments at the top of the output (encounter order, root first, deduplicated — moved, not copied) and insert one-line provenance banners (`// ======== include <lib.scad> ========`) between the inlined sections (default: **on**; SB5007). The downloader of a bundled model sees whose code each section is and under what terms — `--no-bundle-licenses` produces an unannotated bundle, and `--minify`/`--no-preserve-comments` drop the annotations like any comment.
- `--minify`: Remove unnecessary whitespace/comments
- `--dry-run`: Show what would be done without writing output
- `--verbose`: Detailed logging of inlined files and transformations
- `--diff`: Show diff between input and bundled output

## Collision Strategies (`--on-collision`)

A **collision** is two structurally *different* top-level definitions of the same name arriving in one bundle. (Identical copies arriving twice — diamond includes — are merged silently, SB5005, and are not collisions.) The strategy picks which definition the bundled name binds to:

| Strategy | Behavior | Matches OpenSCAD? |
|---|---|---|
| `auto` (default) | Origin-dependent: `include` collisions resolve last-wins with SB3003/SB3004 warnings; colliding `use`-imports stay namespaced (`<filestem>__name`, SB5004). | **Yes** — the correctness-preserving default. |
| `prefix` | Keep *every* colliding definition under a namespaced name; references are rewritten to the definition OpenSCAD's flat scope would have bound (earlier `include` copies survive as dead code, exactly as a shadowed definition does in OpenSCAD). | Yes (rendered geometry). |
| `error` | A genuine collision fails the whole bundle: SB5006, no output, exit code 1. A publish/CI gate for users who want collisions fixed in the sources instead of resolved by the bundler. | n/a — refuses to choose. |
| `keep-last` | Force flat-scope last-wins everywhere, including across `use`d libraries (which `auto` would keep isolated). | For `include`-origin names, yes; forcing it across `use` boundaries breaks library isolation. |
| `keep-first` | Keep the **first** definition of each colliding name (bundle emit order: `use`-imports, then the include-flattened root in document order); silently drop the rest. | **No — deliberately.** See below. |

### Why `keep-first` exists

OpenSCAD's native rule is **last**-wins: a later definition silently stomps an earlier one for the entire scope. Whether that is a feature (the deliberate-override pattern) or a bug (an accidental name clash between unrelated files) is authoring intent the bundler cannot infer. `auto` reproduces OpenSCAD faithfully and warns (SB3003/SB3004); `keep-first` is the repair tool for when those warnings reveal the clash was an *accident* and the **first** definition is the one the model was actually written against:

- **A later `include` stomps a name you depend on, and you can't edit or reorder the sources.** Typical shape: the root includes `myhelpers.scad`, then a vendored third-party library that happens to define a same-named helper. In the multi-file project the right fix is renaming — but the bundle is a build artifact for upload (Thingiverse/MakerWorld), and forking a vendored library to rename one function is a heavy fix for a publishing step. `--on-collision keep-first` pins the name to the definition that came first, without touching the sources.
- **Version-skew diamonds.** Two libraries each vendor *their own copy* of a shared helper file at different versions. The copies are structurally different, so dedup (SB5005) cannot merge them, and last-wins binds every shared name to whichever library happened to be included **later** — possibly the older code. The colliding includes are transitive (inside libraries you don't control), so reordering them is not an option; instead, include the library carrying the newest copy first and bundle with `keep-first` to pin every shared helper to it. This is the same first-wins repair rule C linkers and most module systems apply to duplicate symbols.

**Caveats — why `keep-first` is not the default:**

- The bundle is **intentionally not semantically equivalent** to the original project: it behaves as if every later redefinition never existed. Render-compare before publishing (`openscad -o out.csg original.scad` vs the bundle).
- For a reassigned **variable**, `keep-first` keeps the first assignment with its *original expression*. (Contrast `auto`/`keep-last`, which reproduce OpenSCAD exactly: the parser overwrites a reassigned variable's expression in place, so the **last** expression wins and is emitted at the **first** assignment's position.)
- Like every forced strategy it applies **everywhere**, including `use`-origin names: a colliding `use`-import is kept or dropped un-namespaced, overriding the library isolation `auto` preserves.
- Drops are **silent** by design — no per-name SB3003/SB3004 churn, because the outcome was explicitly chosen. Bundle with the default `auto` and `--dry-run` first to see what collides and which file wins, then decide whether `keep-first` matches your intent.

## Expected Behavior
- Smart resolution of `include` and `use` with cycle detection.
- Clear progress and summary output.
- Excellent error messages with context (coded diagnostics — see [Diagnostics.md](Diagnostics.md)).
- Preserves Customizer parameter comments for platform compatibility.
- Normalizes deprecated constructs to modern equivalents with a warning (`assign`→`let`, `child`→`children`); preserves deprecated built-ins (e.g. `import_stl`) with an informational note.

## C# & OpenSCAD Community Alignment
- NuGet package + `dotnet tool` support.
- Zero-magic transformations with explanatory logs.
- Respect `use` (modules/functions) vs `include` (full execution) semantics.

## Future Extensions
- VS Code extension
- GitHub Action
- Library API for advanced use cases
- **ScadBundler Live** (separate project): A clean, modern web interface allowing drag-and-drop upload of multiple files/folders. Users can resolve naming collisions interactively, view clear explanations of errors, and one-click download or copy the bundled result. The core bundler will expose a clean HTTP/JSON API or WASM library mode to support this.
