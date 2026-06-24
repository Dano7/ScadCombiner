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
- `--minify`: **Minimize bundle size** (Slice 7). An AST-level pass — tree-shakes unreferenced definitions, shortens every identifier, canonicalizes number literals — followed by whitespace/comment stripping. Incidentally unreadable. Keeps the aggregated license header (use `--strip-license` to drop it).
- `--obfuscate`: **Maximize the cost of reverse-engineering** the bundle (Slice 7). Opaque identifiers, reference-transparent indirection, render-inert decoys (uncalled modules + `*`-disabled calls), and decomposed strings (`"ab"`→`str(chr(97),chr(98))`). Output may be *larger* than the input. Mutually exclusive with `--minify` (giving both is a usage error, exit 2). Keeps the license header unless `--strip-license`.
- `--strip-license`: Drop the aggregated license header under `--minify`/`--obfuscate` (for when you own all the sources). Default is to **keep** it — the downloader of a hardened model still gets the legal text.
- `--parameters-first`: **Platform-compatibility workaround (opt-in).** Emit the Customizer parameters *above* the aggregated license header so they lead the file, instead of below it. For Thingiverse, whose Customizer fails to surface parameters that follow a long leading comment block (see **Platform compatibility** below). Comment-relocation only — the geometry is unchanged and the license still appears, just below the parameters. A no-op when there is no aggregated header to move (e.g. `--no-bundle-licenses`) or no Customizer parameters. See [ADR 0002](adr/0002-parameters-first-customizer-hoist.md).
- `--dry-run`: Show what would be done without writing output
- `--verbose`: Detailed logging of inlined files and transformations
- `--diff`: Show diff between input and bundled output
- `--lint`: Report the static source checks OpenSCAD skips at parse time — unknown references (SB3005) and module/function redefinitions (SB3004). **Off by default**: these are static approximations of OpenSCAD's *evaluation-time* behavior (it reads an unknown name as `undef`, warning only if the expression is reached; it silently last-wins on redefinition), so they false-positive on real-world libraries — dead code, short-circuited reads, optional config variables (`is_undef(X) || !X`), last-wins overrides of a library module, and intra-library duplicate definitions. The collision is still resolved without the flag; `--lint` only surfaces the finding. See [Diagnostics.md](Diagnostics.md) SB3004/SB3005.

## Collision Strategies (`--on-collision`)

A **collision** is two structurally *different* top-level definitions of the same name arriving in one bundle. (Identical copies arriving twice — diamond includes — are merged silently, SB5005, and are not collisions.) The strategy picks which definition the bundled name binds to:

| Strategy | Behavior | Matches OpenSCAD? |
|---|---|---|
| `auto` (default) | Origin-dependent: `include` collisions resolve last-wins (silently, matching OpenSCAD — a reassigned **variable** still warns SB3003; module/function redefinition is reported only under `--lint`, SB3004); colliding `use`-imports stay namespaced (`<filestem>__name`, SB5004). | **Yes** — the correctness-preserving default. |
| `prefix` | Keep *every* colliding definition under a namespaced name; references are rewritten to the definition OpenSCAD's flat scope would have bound (earlier `include` copies survive as dead code, exactly as a shadowed definition does in OpenSCAD). | Yes (rendered geometry). |
| `error` | A genuine collision fails the whole bundle: SB5006, no output, exit code 1. A publish/CI gate for users who want collisions fixed in the sources instead of resolved by the bundler. | n/a — refuses to choose. |
| `keep-last` | Force flat-scope last-wins everywhere, including across `use`d libraries (which `auto` would keep isolated). | For `include`-origin names, yes; forcing it across `use` boundaries breaks library isolation. |
| `keep-first` | Keep the **first** definition of each colliding name (bundle emit order: `use`-imports, then the include-flattened root in document order); silently drop the rest. | **No — deliberately.** See below. |

### Why `keep-first` exists

OpenSCAD's native rule is **last**-wins: a later definition silently stomps an earlier one for the entire scope. Whether that is a feature (the deliberate-override pattern) or a bug (an accidental name clash between unrelated files) is authoring intent the bundler cannot infer. `auto` reproduces OpenSCAD faithfully (a reassigned variable warns SB3003; run with `--lint` to also surface module/function redefinitions, SB3004); `keep-first` is the repair tool for when those findings reveal the clash was an *accident* and the **first** definition is the one the model was actually written against:

- **A later `include` stomps a name you depend on, and you can't edit or reorder the sources.** Typical shape: the root includes `myhelpers.scad`, then a vendored third-party library that happens to define a same-named helper. In the multi-file project the right fix is renaming — but the bundle is a build artifact for upload (Thingiverse/MakerWorld), and forking a vendored library to rename one function is a heavy fix for a publishing step. `--on-collision keep-first` pins the name to the definition that came first, without touching the sources.
- **Version-skew diamonds.** Two libraries each vendor *their own copy* of a shared helper file at different versions. The copies are structurally different, so dedup (SB5005) cannot merge them, and last-wins binds every shared name to whichever library happened to be included **later** — possibly the older code. The colliding includes are transitive (inside libraries you don't control), so reordering them is not an option; instead, include the library carrying the newest copy first and bundle with `keep-first` to pin every shared helper to it. This is the same first-wins repair rule C linkers and most module systems apply to duplicate symbols.

**Caveats — why `keep-first` is not the default:**

- The bundle is **intentionally not semantically equivalent** to the original project: it behaves as if every later redefinition never existed. Render-compare before publishing (`openscad -o out.csg original.scad` vs the bundle).
- For a reassigned **variable**, `keep-first` keeps the first assignment with its *original expression*. (Contrast `auto`/`keep-last`, which reproduce OpenSCAD exactly: the parser overwrites a reassigned variable's expression in place, so the **last** expression wins and is emitted at the **first** assignment's position.)
- Like every forced strategy it applies **everywhere**, including `use`-origin names: a colliding `use`-import is kept or dropped un-namespaced, overriding the library isolation `auto` preserves.
- Drops are **silent** by design — no per-name SB3003/SB3004 churn, because the outcome was explicitly chosen. Bundle with the default `auto` and `--dry-run` first to see what collides and which file wins, then decide whether `keep-first` matches your intent.

## Minify & Obfuscate (`--minify` / `--obfuscate`)

Two output-hardening profiles share one engine and one **non-negotiable correctness bar**: the hardened
bundle must render **byte-identical CSG** to the original (verified against the official OpenSCAD binary).
They only ever apply *value-preserving, CSG-tree-preserving* transforms (renaming, tree-shaking, literal
re-spelling, reference-transparent indirection, render-inert decoys, string decomposition) — never
restructuring that merely yields the same *solid*, which the byte-identical bar cannot prove. Full
rationale in [slices/Slice-7-Minify-Obfuscate.md](slices/Slice-7-Minify-Obfuscate.md).

- **Customizer parameters keep their names — but only at the top.** OpenSCAD's Customizer still lists the
  model's real knobs (`wall_thickness`, …) with their original names. Each parameter is then assigned to a
  generated alias *immediately* and that alias is used everywhere after, so the meaningful name appears
  exactly once. (`--minify` keeps each top-level statement on its own line so the line-based Customizer
  extraction still works.)
- **Customizer comments are kept.** The comments OpenSCAD's Customizer reads off each parameter — its
  group header (`/* [Section] */`), its description line, and its inline slider/range annotation
  (`// [1:20]`) — survive both profiles, so the bundled model's Customizer groups and labels its knobs
  exactly like the unbundled file. Only these comments are kept; ordinary comments and the long library
  headers still drop (the latter via `--strip-license`).
- **Deterministic with avalanche.** Same input → byte-identical output (good for reproducible builds and
  goldens), yet a one-character source change reshuffles *every* generated name — so you can't diff two
  versions to learn what changed. Always on; there is no stable-name escape hatch.
- **License preserved by default.** The aggregated license/attribution header survives both profiles (the
  downloader still gets the legal text); per-section provenance banners and ordinary comments (those not
  driving the Customizer) are dropped. `--strip-license` opts out.
- **Mutually exclusive.** `--minify --obfuscate` together is a usage error (exit 2).

## Platform compatibility (Thingiverse): `--parameters-first`

Bundles render correctly on MakerWorld and generally on Thingiverse, but Thingiverse's Customizer is
**out of spec** in one observable way: when a long run of comments precedes the first Customizer
parameter, its parameter parser fails to surface the parameters at all. The default bundle layout puts
exactly such a run there — the aggregated license/attribution header (`--bundle-licenses`, default on)
is hoisted to the very top, *above* the parameters.

`--parameters-first` flips that one attachment: the parameters lead the file and the license header
follows them (`parameters → license header → /* [Hidden] */ → body`). It is the automatic form of the
manual fix — moving the parameters above the license — that makes Thingiverse list them correctly.

- **Opt-in by design.** This is a workaround for one platform's non-conforming parser, not a better
  default; ScadBundler's default keeps attribution at the top (credits lead). Turn it on only when you
  need it.
- **Comment-relocation only — geometry unchanged.** The parameter prologue is already hoisted to the
  top and protected by the inliner; the flag only moves *where the header comments are emitted*. No
  statement reorders, so the rendered CSG is byte-identical (proven against the official binary).
- **The license is relocated, never dropped.** It still appears in the bundle, just below the
  parameters — including under `--minify`/`--obfuscate` (sticky, unless `--strip-license`). A casual
  viewer therefore sees the Customizer knobs first and the attribution second.
- **Composes with `--minify`.** Thingiverse also appears to enforce a ~1–2 s render-time limit (no
  public docs); a model too complex to render in time fails regardless, which is outside this tool's
  scope. `--minify` is the only lever, and `--minify --parameters-first` is the combination for a
  complex model that also needs its Customizer read correctly.

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
