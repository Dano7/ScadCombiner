# ScadBundler Live — Development Slices

Same approach as the CLI ([../Development-Slices.md](../Development-Slices.md)): build **incrementally,
test-driven**, each slice a working, testable milestone. Full specs live in [slices/](slices/).

The keystone is **W0** — all logic (entry-point inference, dependency status, bundling) lives in a
browser-free Core/Workspace facade covered to the Constitution's ≥95% bar. W1–W3 are a **thin Blazor
shell** over it. This split is deliberate: it keeps the hard-to-test UI layer thin and the testable logic
maximally comparable across implementations.

## Build order

| Slice | Milestone | Project | Tested to ≥95%? |
|---|---|---|---|
| **W0** | Workspace facade & in-memory FS | `ScadBundler.Core/Workspace/` | **Yes** (xUnit, browser-free) |
| **W1** | Blazor shell + bundle MVP (drop → bundle → copy/download) | `web/ScadBundler.Web/` | No (thin shell; smoke tests) |
| **W2** | Dependency UX & friendly errors (missing-file drop targets, entry-point re-designate, editable main, problems panel) | `web/ScadBundler.Web/` | No (bUnit on the status model) |
| **W3** | Options, polish & deploy (flags, a11y, responsive, static publish) | `web/ScadBundler.Web/` | No |
| **W4** | *(Stretch, v2)* openscad-wasm 3D preview + Customizer | `web/ScadBundler.Web/` | — (deferred) |

## Slice W0 — Workspace facade & in-memory file system  ·  **spec ready**

Full spec: **[slices/Slice-W0-Workspace-Facade.md](slices/Slice-W0-Workspace-Facade.md)**.

**Scope**: a new `src/ScadBundler.Core/Workspace/` area — `InMemoryFileSystem` (virtual `/`-rooted tree
over uploaded files), `ProjectAnalyzer` (entry-point inference + dependency tree + missing-reference
report, with layout inference so the bundle resolves exactly as the analysis predicted), `WebBundler`
(browser-friendly `Bundler.Bundle(IFileSystem)` + `Emitter.Emit` → text + stats), and the
JSON-serializable DTOs (`ProjectAnalysis`, `DependencyNode`, `MissingReference`, `WebBundleResult`,
`BundleStats`, `DiagnosticDto`). No new `SBxxxx` codes. **Exit**: zero-warning build; green xUnit;
≥95% line coverage on `Workspace/`; bundle-parity with the CLI proven byte-identical.

## Slice W1 — Blazor shell + bundle MVP  ·  **spec ready**

Full spec: **[slices/Slice-W1-Blazor-Shell.md](slices/Slice-W1-Blazor-Shell.md)**.

**Scope**: `web/ScadBundler.Web` Blazor WebAssembly project (.NET 10) added to the solution; the landing
shell (title + "why" blurb, fast first paint while the runtime loads); the single smart drop zone with all
three **structure-preserving ingestion modes** — folder (`webkitdirectory` + drag-drop entries API), loose
files, and `.zip` (BCL `ZipArchive`, no JS); the file list with entry-point badge and per-file status;
live bundling wired to W0; **Copy** and **Download**. **Exit**: `dotnet run` serves it; folder, files, and
zip uploads each bundle a real multi-file project and download it byte-identical to the CLI; bUnit smoke
tests pass.

## Slice W2 — Dependency UX & friendly errors  ·  **spec ready**

Full spec: **[slices/Slice-W2-Dependency-UX.md](slices/Slice-W2-Dependency-UX.md)**.

**Scope**: missing-reference panel rendered as drop targets ("drop `lib.scad` here"); the inferred entry
point surfaced with manual **re-designate** / **replace** of the main file; an editable, debounced
main-file `<textarea>`; the problems panel (`file : line : col` + message + a friendly per-`SBxxxx`
explanation), with **SB4001 never shown as an error** (it drives the missing-file panel); live
used/unused/missing highlighting when the main file changes; a **read-only structure tree** and a
**basename-conflict picker** for `Ambiguous` references (one-click pick / inline path — never an editable
tree). **Exit**: add-a-missing-file, swap-the-main-file, and resolve-a-basename-conflict flows work
end-to-end; bUnit tests on the status/diagnostics view models.

## Slice W3 — Options, polish & deploy  ·  **spec ready**

Full spec: **[slices/Slice-W3-Options-Polish-Deploy.md](slices/Slice-W3-Options-Polish-Deploy.md)**.

**Scope**: the options expander — a "Remove provenance banners / license aggregation" checkbox
(default off), a **Normal / Minify / Obfuscate** radio (mutually exclusive) with the educational
tooltip, and an **Advanced** sub-section (collision strategy, strip-license, preserve-comments); every
option re-bundles live and maps to the matching CLI flag; responsive layout, accessibility, empty/error
states; the deploy pipeline (generic static `wwwroot` publish with trimming + Brotli; host-specific
base-href/CI chosen here); update the docs [README.md](../README.md) companion link. **Exit**: each
option matches its CLI equivalent; the published build loads and works from a static host; a11y pass.

## Slice W4 — *(Stretch, v2)* openscad-wasm preview + Customizer  ·  **deferred**

Full spec stub: **[slices/Slice-W4-Preview-Stretch.md](slices/Slice-W4-Preview-Stretch.md)**. Out of v1
scope; documented so v1 is architected to feed it (the bundle text is the input). Lazy-load
openscad-wasm (~10–30 MB), render the bundle to a mesh (three.js), read Customizer parameters. Not
detailed.

## Slice W5 — Large-project UX: structure, smart resolution & responsiveness  ·  **design / proposed**

Full spec: **[slices/Slice-W5-Large-Project-UX.md](slices/Slice-W5-Large-Project-UX.md)**. Surfaced by the
[ParametricCompoundPlanetary + BOSL2 validation](../Real-World-Validation.md). Makes the page hold up on
big, multi-library, loosely-uploaded projects: an **organize layer** (editable structure tree —
create-folder / move / set-path, all expressed as upload `Name` edits, no Core change) so the user can
supply structure the basename fixpoint can't infer; **smarter resolution** (library-root learning,
sub-path-aware matching, visible auto-placement provenance) and **common-library presets** (BOSL2 /
NopSCADlib / dotSCAD on demand); and **responsiveness** (get the recompute off the UI thread with
yield-based progress + debounce/cancel, then a Web Worker; share one parse across analyzer/loader; lazy
emit) so a ~2.9 MB / 33-file bundle never freezes the browser. **Exit**: a colliding loose multi-library
upload bundles without re-zipping; no "page unresponsive" prompt on a BOSL2-scale project; bundle output
and `SBxxxx` codes unchanged; CLI byte-parity preserved.
