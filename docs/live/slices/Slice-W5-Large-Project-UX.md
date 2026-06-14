# Slice W5 — Large-Project UX: Structure, Smart Resolution & Responsiveness

**Status**: **design / proposed** — surfaced by the [ParametricCompoundPlanetary + BOSL2 real-world
validation](../../Real-World-Validation.md) (2026-06-14). Not yet scheduled.
**Project**: `web/ScadBundler.Web/` + `src/ScadBundler.Core/Workspace/` + `tests/ScadBundler.Web.Tests/`.
**Depends on**: [Slice-W2](Slice-W2-Dependency-UX.md) (dependency UX, conflict picker, structure tree),
[Slice-W3](Slice-W3-Options-Polish-Deploy.md).
**Read with**: [../Spec.md](../Spec.md) §3.2–§3.4, [../Design.md](../Design.md) §3.2 (WorkspaceController).

W2 made the page *smart and forgiving* for small projects. W5 makes it **hold up on big, multi-library,
loosely-uploaded projects** — the BOSL2-scale reality — along three axes: **let the user supply structure**
the analyzer can't infer, **guess better and offer common libraries**, and **stay responsive** while a
large bundle computes in WASM.

---

## 0. Why (evidence)

The BOSL2 validation ([Real-World-Validation.md §1.2–1.4](../../Real-World-Validation.md)) established:

- For a project with **no duplicate basenames**, a loose (structure-less) upload already bundles
  **byte-identical** to a structured one — the `ProjectAnalyzer` basename fixpoint handles it. So W5 is
  **not** a core-resolution rewrite; it targets the cases the fixpoint *can't* settle and the *experience*
  of large bundles.
- The genuine gaps are: **basename collisions** across libraries/user files (deliberately left to a
  per-reference pick), **absolute-path references** (unplaceable), no way to **reorganize after upload**
  (the W2 structure tree is display-only, by design), no **common-library shortcut** (40 files by hand),
  and a **UI-thread freeze** with no feedback on a ~2.9 MB / 33-file bundle in WASM.

**Non-goals**: changing any Core bundling semantics or `SBxxxx` codes; a full in-browser file manager; a
package registry. W5 is upload-ergonomics + responsiveness only — the bundle output is unchanged.

---

## Part A — Let the user provide structure

The W2 `StructureTree` is **display-only** ("structure comes from how files were uploaded", W2 §6). That's
the gap: when the fixpoint can't infer placement (collisions, absolute paths, mixed libraries), the user
has no way to *say* where a file belongs short of re-zipping. Add a minimal, reversible **organize layer**
over the upload set — no Core change, all expressed as `UploadedFile.Name` (the virtual sub-path) edits.

### A1. Editable structure tree (`StructureEditor`)
Promote the read-only tree to an editor with exactly three operations, each a new `WorkspaceController`
intent that re-keys uploads and re-analyzes:

| Operation | Intent | Effect |
|---|---|---|
| **New folder** | `CreateFolder(path)` | a virtual folder (empty until files move into it) |
| **Move file(s)** | `MoveUploads(names, intoFolder)` | re-add each upload under `folder/<basename>` (drag within the tree, or multiselect → "Move to…") |
| **Set a file's path** | `SetUploadPath(name, newName)` | rename the virtual path directly (the existing `ConflictPicker` "…or set its path", generalized to any file) |

Because placement is *only* the `Name`, every move is just `AddOrReplace` with a new name + `Remove` of
the old — **no new facade primitive**, no Core change, fully undoable by moving back.

### A2. "Put these under a folder" bulk action on drop
When a loose multi-file (or multi-folder-less) drop arrives, offer a one-click **"these N files look like a
library — put them under `____/`"** banner that pre-fills a folder name inferred from the references that
need them (e.g. a `<BOSL2/std.scad>` reference ⇒ suggest `BOSL2`). Accepting calls `MoveUploads`. This
directly converts the common "I dragged a library's loose files" mistake into a correct layout in one
click, and is the fastest fix for 1.2.

### A3. Make guesses visible (provenance)
Today the basename fixpoint silently aliases. Surface it: in the file list / structure tree, badge each
**auto-placed** file ("resolved as `BOSL2/std.scad` by name — ✎ change") so a *wrong* guess (a collision
the fixpoint happened to pick, or a future heuristic) is visible and one-click correctable, never silent.

---

## Part B — Guess better & offer common libraries

### B1. Library-root learning (disambiguation heuristic)
Extend `ResolveByBasename`: once one reference `<BOSL2/a.scad>` resolves to a loose `a.scad`, record the
mapping *loose-root → `BOSL2/`*. For a later **ambiguous** basename, prefer the candidate consistent with
an already-learned library root before falling back to "ambiguous → ask". This resolves the common case
where two libraries collide on a name but only one is the library currently being referenced. Keep it
conservative: only auto-place when the learned root makes the match **unique**; otherwise still ask (A3
provenance keeps it honest).

### B2. Sub-path-aware matching
Prefer a candidate whose *trailing sub-path* matches the reference (`x/y/foo.scad` beats a bare
`foo.scad`) before considering it ambiguous — folder/zip uploads already carry this; this lets a
partially-structured loose upload self-disambiguate.

### B3. Common-library presets (`LibraryPresets`)
A small picker — "Add a library: ◻ BOSL2  ◻ NopSCADlib  ◻ dotSCAD" — that injects a **version-pinned,
bundled** copy of the library's files (as `UploadedFile`s under the correct sub-paths) so the user supplies
only their own model. Implementation notes:
- Ship the libraries as static assets fetched on demand (keeps the initial WASM payload small); show the
  pinned version and a provenance/licence note (these aggregate into the bundle header via SB5007).
- This does **not** help users whose *own* libraries live in subfolders — Part A covers that — but it
  removes the 40-files-by-hand burden for the popular libraries that drive most real projects.

---

## Part C — Responsiveness on big bundles

**Problem**: `WorkspaceController.Recompute` runs the whole pipeline **synchronously on the UI thread**;
Blazor WASM is single-threaded by default, so a ~2.9 MB / 57-file project blocks long enough to trigger
the browser's "Page isn't responding" prompt, with **no feedback**. The CLI is ~instant (native). Two
fronts: stop blocking, and do less.

### C1. Get off the UI thread + show progress
- **Yield-based progress (smallest change)**: split `Recompute` into phases (analyze → load → semantic →
  inline → emit) and `await Task.Yield()` between them, raising a `Progress(phase, fileCount)` event so a
  **determinate progress bar** ("Analyzing 57 files…", "Inlining 33…", "Emitting…") paints and the page
  stays alive. Even cooperative yielding removes the "unresponsive" prompt.
- **Web Worker offload (robust)**: run the pipeline in a worker (a second Blazor WASM runtime, or a JS
  worker hosting the `dotnet` runtime) and post results back, so the UI thread never blocks at all. Larger
  effort; gated behind C1's instrumentation so we know it's worth it.
- **Debounce + cancel**: coalesce rapid intents (uploads, main-file keystrokes) and cancel an in-flight
  recompute when a newer one supersedes it (`CancellationToken` threaded through the phase loop), so a big
  project doesn't queue N full bundles while the user is still dropping files.

### C2. Do less work
- **One parse, not three**: `ProjectAnalyzer` parses every file for inference, then `SourceLoader` parses
  them all again for the bundle, and `ProjectDiagnostics` analyzes a third time. Share a parse cache keyed
  by content across analyzer + loader so each file is parsed once per recompute.
- **Incremental recompute**: an edit to the root file shouldn't re-parse the 40 unchanged library files —
  cache parses by content hash (the analyzer already caches by text; extend it across recomputes) so only
  the changed file is reprocessed.
- **Lazy emit**: skip the emit/stats pass until the user actually asks to view/copy/download, so analysis
  feedback (tree, problems) appears before the full 2.9 MB string is materialized.

### C3. Set expectations
Until offload lands, show a one-time "Large project — this can take a few seconds in your browser; the
command-line tool is instant if you have it" note for projects above a file/byte threshold, with a link to
the CLI. Honest framing beats a silent freeze.

---

## Suggested sequencing

1. **C1 yield-based progress + debounce/cancel** — highest impact-per-effort; kills the "frozen" symptom.
2. **A1/A2 organize UI** — unblocks the genuine structure-less cases and multi-library collisions.
3. **A3 provenance + B1/B2 smarter matching** — fewer manual picks, no silent mis-resolution.
4. **B3 library presets** — removes the bulk-upload burden for popular libraries.
5. **C2 shared-parse / incremental** and **C1 Web Worker** — deeper performance, measured against C1's
   instrumentation.

## Exit criteria (when scheduled)
- A loose, multi-library upload with a basename collision can be made to bundle **without re-zipping**, via
  the organize UI, in a handful of clicks; the resulting bundle is byte-identical to the structured upload.
- A BOSL2-scale project shows continuous progress and never triggers the browser "unresponsive" prompt.
- No change to bundle output or `SBxxxx` codes; bundle-parity with the CLI still byte-identical
  ([BundleParityTests](../../../tests/ScadBundler.Core.Tests/Workspace/BundleParityTests.cs)).
- bUnit coverage on the new intents (`CreateFolder`/`MoveUploads`/`SetUploadPath`, progress events) at the
  view-model level, per the W-slice testing convention.

## Open questions / iteration notes
- Web Worker + Blazor WASM threading maturity on target browsers — measure before committing to C1's
  worker path; yield-based progress may be "enough".
- Library-preset hosting, versioning, and licence display — pin and surface provenance so the SB5007 header
  stays accurate.
- B1/B2 heuristics must stay conservative: prefer **ask** over a confident wrong guess; A3 provenance is the
  safety net that makes any heuristic acceptable.
