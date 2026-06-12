# Slice W2 — Dependency UX & Friendly Errors

**Status**: spec ready (not started).
**Project**: `web/ScadBundler.Web/` + `tests/ScadBundler.Web.Tests/`.
**Depends on**: [Slice-W1](Slice-W1-Blazor-Shell.md).
**Read with**: [../Spec.md](../Spec.md) §3.2–§3.4 (inference UX, missing refs, replace/edit), §6 (algorithms).

W1 proved the happy path. W2 makes the page **smart and forgiving**: it tells the user exactly what's
missing, lets them fix it by dropping the file, lets them swap or edit the main file, and explains real
syntax errors helpfully — while never scaring them with "missing file" as an error.

---

## 1. Goal

Turn the static file list into the interactive dependency experience from the vision: missing references
as drop targets, entry-point re-designation, an editable main file with live re-analysis, and a friendly
problems panel.

**Non-goals (this slice):** the options panel and deploy (W3); preview (W4); a rich code editor (textarea
only).

---

## 2. Deliverables / behavior

1. **Missing-reference rows as drop targets** (`MissingRow`). Each `ProjectAnalysis.Missing` entry renders
   as a ⚠ row: the path as written + "needed by `main.scad`" + a small drop target ("drop this file
   here"). Dropping the matching file (here, or anywhere on the main zone) adds it, re-analyzes, and — if
   the tree completes — bundles. Output stays disabled while any non-font reference is unresolved, with a
   "still need N file(s)" summary. Fonts are shown as ⓕ and never block.
2. **Entry-point surfacing & override.** When `InferredRoot` is set, badge it and show its tree. When it's
   `null` (ambiguous), show the `EntryPointCandidates` with a prompt: "Which file is your model?" Clicking
   any file → `WorkspaceController.SetRoot` → re-analyze. A "★ main" affordance lets the user re-designate
   at any time.
3. **Replace / edit the main file** (`MainFileEditor`). A debounced (~200 ms) `<textarea>` bound to the
   current root's text. Edits call `WorkspaceController.EditMainFile`, which replaces that upload and
   re-analyzes — so newly-referenced libraries light up as needed, and now-unused ones de-emphasize.
   Dropping a different file as the main file also works.
4. **Live used / unused / missing highlighting.** On every change, the file list reflects, per uploaded
   file: **used** (in the current root's dependency tree), **unused** (uploaded but not reached from the
   root), or **missing** (referenced, not uploaded). Drives the vision's "highlight as used/unused/
   missing".
5. **Problems panel** (`ProblemsPanel`). Renders the non-missing `DiagnosticDto`s grouped by severity:
   `file : line : col` + message + an optional **friendly explanation** keyed by `Code` (a small
   client-side `SBxxxx → sentence` map; absent code → just the message). **SB4001 is never shown here** —
   it is the file list's ⚠ rows. (The facade already filters SB4001 from `Diagnostics`; the panel must not
   reintroduce it.)

---

## 3. Friendly-explanation map (UI-only, no new codes)

A static `Dictionary<string,string>` in the web app mapping common codes to one plain sentence, e.g.:

| Code | Friendly line |
|---|---|
| SB2001/SB2002/SB2004/SB2005 | "There's a typo or missing punctuation here — OpenSCAD couldn't read this line." |
| SB3003 | "Two files set the same value; the later one wins (this is usually fine)." |
| SB3004 | "Two files define the same module/function; the later one is used." |
| SB4002 | "These files include each other in a loop — remove one of the references." |
| SB5004 | "A library name was renamed to avoid a clash — your model still works." |

This is **presentation only** — it invents no diagnostic codes and changes no Core behavior. Codes not in
the map fall back to the raw (already human-facing) message.

---

## 4. Scope (In / Out)

**In:** `MissingRow` + drop-to-resolve; ambiguous-root picker + click-to-promote; `MainFileEditor`
(debounced); used/unused/missing classification in `FileList`; `ProblemsPanel` with the friendly map;
folder drop wired (`webkitGetAsEntry`) so relative paths feed layout inference.

**Out:** options/flags (W3); deploy (W3); preview (W4); Monaco.

---

## 5. Test plan

- **bUnit**: `MissingRow` renders one target per `Missing` entry with the right `NeededBy`; resolving a
  missing file flips it to ✓ and enables output; `ProblemsPanel` shows SB2xxx/SB3xxx but **never** SB4001;
  the ambiguous-root case lists candidates and `SetRoot` re-roots; `MainFileEditor` edit triggers a
  re-analyze (debounce honored).
- **View-model unit tests** (can live in `ScadBundler.Core.Tests` if the classification is a pure helper):
  used/unused/missing partition for a given `ProjectAnalysis` + upload set.
- **Manual acceptance**: drop only `main.scad` of a real project → see its libs listed as needed → drop
  them one by one → bundle appears; then edit the textarea to remove an `include` → that lib flips to
  "unused"; add a new `use <foo.scad>` → "foo.scad" appears as needed.

---

## 6. Exit criteria

- [ ] Dropping a model with missing libraries lists each missing reference (with "needed by"); dropping
      the files resolves them and produces the bundle.
- [ ] Ambiguous uploads prompt for the main file; clicking a file re-roots and re-analyzes.
- [ ] Editing the main-file textarea re-analyzes live and updates used/unused/missing highlighting.
- [ ] The problems panel shows real syntax/semantic issues with `file:line:col` + friendly text and
      **never** shows SB4001 as an error.
- [ ] bUnit tests for the missing-file, re-root, and diagnostics-filtering paths pass.
