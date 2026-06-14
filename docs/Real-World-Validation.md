# Real-World Validation — Findings Log

A living record of issues surfaced by running ScadBundler against **real, third-party projects** (as
opposed to the synthetic test corpus). Each entry states the symptom, the root cause, and the resolution
status, so a regression or a deferred item is never lost.

---

## Case 1 — `ParametricCompoundPlanetary.scad` + BOSL2 (2026-06-14)

**Subject**: `C:\git\hub\ParametricCompoundPlanetary.scad`, a compound planetary-gear model that
`include`s BOSL2 (`include <BOSL2/std.scad>`, `include <BOSL2/gears.scad>`) from a relative library path
(`C:\git\hub\BOSL2`). 57 source files in play; the bundle is ~2.87 MB with 33 files inlined.

**Headline result**: the bundle **renders without error or warning in OpenSCAD** — the end-to-end pipeline
(loader → lexer → parser → semantic → inliner → emitter) is correct on a large, real, deeply-`include`d
library, including the BOSL2 builtin-override and recursive-`function`-literal hazards.

The run surfaced four follow-up issues, below.

### 1.1 — False bundler warnings (SB3004 / SB3005) · **RESOLVED**

**Symptom**: the bundler emitted ~43 warnings on input OpenSCAD accepts silently, e.g.

```
WARNING: SB3005 BOSL2/gears.scad:12:76: Unknown variable 'BOSL2_NO_STD_WARNING'.
WARNING: SB3005 BOSL2/gears.scad:4687:45: Unknown variable 'helical'.
WARNING: SB3004 ParametricCompoundPlanetary.scad:87:1: module 'pie_slice' is redefined; the last definition wins.
WARNING: SB3004 attachments.scad:3312:1: function '_get_cp' is redefined; the last definition wins.
```

**Root cause** — these were **static approximations of OpenSCAD's dynamic (evaluation-time) behavior**,
verified against the OpenSCAD C++ source (`C:\git\hub\openscad`):

- **SB3005 (unknown reference)** mirrored OpenSCAD's `Context::lookup_variable` / `lookup_function` /
  `lookup_module` "Ignoring unknown …" warnings — but those fire **only when the symbol is actually
  evaluated during rendering**. A static bundler can't know reachability, so it false-positived on:
  - short-circuit-guarded reads — `is_undef(_BOSL2_STD) && (… || !BOSL2_NO_STD_WARNING)` never evaluates
    the right operand;
  - dead code — `gear_shorten_skew` references a parameter `helical` that doesn't exist, but the function
    is never called, so OpenSCAD never looks it up;
  - optional config variables a library probes for (`BOSL2_NO_STD_WARNING`).
- **SB3004 (module/function redefinition)** has **no OpenSCAD analogue at all**: OpenSCAD's flat
  `LocalScope` silently last-wins, and `parser.y handle_assignment` warns only for *variable* reassignment
  (SB3003). The "redefinitions" were the routine "user overrides a library module" pattern (`pie_slice`,
  `right_triangle`, `reverse`) and genuine intra-library duplicates (BOSL2's `attachments.scad` defines
  `_get_cp` twice; `comparisons.scad` `_sort_vectors`; `lists.scad` `_list_shape_recurse`) — all of which
  OpenSCAD accepts without comment.

**Fix** — both are now treated as **opt-in static source-lint**, suppressed by default and surfaced only
under a new `--lint` flag (`BundleOptions.Lint`, filtered centrally in `Bundler.IsStaticLint`). The
collision is still **resolved** either way (last-wins / namespacing); `--lint` only controls whether it is
*reported*. SB3003 (variable reassignment) is unchanged — OpenSCAD does warn on it, and it is a deliberate
maker-facing signal. Default output now matches OpenSCAD's silence; the 43 findings return verbatim under
`--lint`. See [Diagnostics.md](Diagnostics.md) SB3004 / SB3005 and the CLI `--lint` flag.

### 1.2 — Web: loose-file upload "renders incorrectly" · **INVESTIGATED — gaps tracked in W5**

**Symptom (reported)**: dragging the project files into ScadBundler Live **without** directory structure
yields a bundle that does *not* render correctly; uploading a `.zip` (or a folder) with the correct
relative paths bundles perfectly.

**Investigation** — reproduced headlessly against the real BOSL2 via `ProjectAnalyzer` /
`WebBundler` (browser-free Core): the project + all 40 BOSL2 files, uploaded **loose (basename only)**,
produces a bundle **byte-identical** to the same files uploaded **structured (`BOSL2/…` sub-paths)** —
33 files inlined, 0 missing, 0 ambiguous. The existing **basename fixpoint** (`ProjectAnalyzer.
ResolveByBasename`) already places an alias for every unresolved `<BOSL2/foo.scad>` reference at the path
the loader looks for, and this project has **no duplicate basenames**, so resolution is unambiguous.

**Conclusion**: for this specific project the core resolution is *not* the problem. The reported symptom
most likely comes from one of the basename fixpoint's genuine limits — which the W5 UX work targets:

- **Basename collisions** (`matches.Count ≥ 2`) are deliberately **not** auto-placed — they become an
  `Ambiguous` row needing a per-reference pick. Two libraries (e.g. BOSL2 + NopSCADlib) or a user file
  sharing a name with a library file will block or mis-resolve a loose upload.
- **Absolute-path references** (`include </abs/foo.scad>`) can't be satisfied by basename placement
  (`AliasTarget` returns `null`) and stay missing.
- **Partial / interrupted uploads** — a loose multi-select drag that misses files, or a bundle abandoned
  during the WASM freeze (see 1.4), yields an incomplete result that *looks* like a resolution bug.

**Proposed UX** (see [Slice-W5](live/slices/Slice-W5-Large-Project-UX.md)): a post-upload **organize**
affordance (create folder / move files / set a file's path) so the user can supply the structure the
fixpoint can't infer, plus clearer "resolved by guessing" provenance so silent mis-resolution is visible.

### 1.3 — Web: smarter reference resolution & common-library presets · **PROPOSED — W5**

Two complementary ideas to reduce the upload burden:

- **Guess better**: when one `<Lib/a.scad>` reference resolves to a loose `a.scad`, *learn the library
  root* and prefer same-library placements for the rest, disambiguating some collisions automatically;
  treat a sub-path hint in the reference (`BOSL2/`) as a tiebreaker.
- **Common-library presets**: let the user pick a bundled, version-pinned library (BOSL2, NopSCADlib,
  dotSCAD) instead of hand-uploading ~40 files. (Does not help users with their *own* libraries in
  subfolders — the organize UI in 1.2 covers that.)

### 1.4 — Web: WASM is slow & the page appears frozen · **PROPOSED — W5**

**Symptom**: on a project this large the WASM build blocks the UI thread long enough that the browser
shows "Page isn't responding… Wait / Exit Page". There is no progress feedback, so it looks broken. The
native CLI is effectively instant on the same input — this is a WASM + single-threaded-Blazor limitation,
not an algorithmic one, though there is also room to do less work.

**Proposed** (see [Slice-W5](live/slices/Slice-W5-Large-Project-UX.md)): move the recompute off the UI
thread (Web Worker / async yielding), show a determinate progress indicator tied to pipeline phases, and
cut redundant work (the analyzer parses every file for inference and the loader parses them again; a large
loose upload also re-runs the whole fixpoint on every keystroke/upload).

---

## How these were verified

- **OpenSCAD ground truth**: behavior of unknown-reference and redefinition diagnostics confirmed in the
  checked-out OpenSCAD source — `src/core/Context.cc` (eval-time `lookup_*` warnings), `src/core/parser.y`
  `handle_assignment` (variable-only reassignment warning), `src/core/LocalScope.cc` (silent last-wins).
- **Bundler behavior**: `dotnet run --project src/ScadBundler -- bundle <root> -o <out>` with and without
  `--lint`; default output is clean (only the SB5007 header-aggregation info), `--lint` restores all 43.
- **Web resolution**: reproduced loose-vs-structured upload through `ProjectAnalyzer` + `WebBundler`
  headlessly (no browser); bundles were byte-identical. A throwaway probe was used and removed.
