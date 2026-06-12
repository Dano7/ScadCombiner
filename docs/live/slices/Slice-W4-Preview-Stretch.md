# Slice W4 — (Stretch, v2) openscad-wasm Preview + Customizer

**Status**: **deferred (v2)** — documented so v1 is architected to feed it, not detailed for
implementation. Decision (this session): the 3D preview is **out of v1 scope**.

**Project**: `web/ScadBundler.Web/` (future).
**Depends on**: [Slice-W3](Slice-W3-Options-Polish-Deploy.md) (a shipped v1).

---

## 1. Idea

Let the user **see** their bundle and tweak its Customizer parameters right on the page — closing the loop
from "drag files in" to "preview the model" without OpenSCAD installed.

## 2. Why it's plausible

The official OpenSCAD engine has a **WebAssembly build** (the same family that powers the OpenSCAD web
playground). It can evaluate `.scad` source in-browser and render to a mesh, and Customizer parameters can
be read from the source. The v1 architecture already produces the one input it needs: **the bundle text**.

## 3. Why it's deferred

- **Large dependency** (~10–30 MB of WASM) — at odds with v1's "fast page, tiny payload" promise. Must be
  **lazy-loaded** only when the user opens the preview, never on first paint.
- **New surface**: a three.js (or `<model-viewer>`) viewer, a render worker, mesh handling, a Customizer
  parameter UI — a slice's worth of work orthogonal to bundling correctness.
- **Validation cost**: licensing and version-pinning of the openscad-wasm artifact need their own review.

## 4. Sketch (for when it's picked up)

1. **Lazy module**: load openscad-wasm on demand (a "Preview" button), off the critical path; show a
   one-time download indicator.
2. **Render**: feed `WebBundleResult.Text` to the engine in a worker → mesh → display with three.js;
   re-render (debounced) when the bundle changes.
3. **Customizer**: parse the bundle's Customizer parameters (the same `/* [Group] */` + `// [min:max]`
   annotations the bundler already preserves) into a small form; edits re-render the preview. **Do not**
   let preview edits mutate the downloaded bundle unless explicitly applied.
4. **Optional**: an in-browser *differential* sanity check (render the original root vs. the bundle and
   compare) — the ultimate "the bundle is faithful" reassurance, but heavy; weigh against payload.

## 5. Architectural hooks to keep in v1 (so this stays cheap later)

- Keep the bundle text the single source of truth in `WorkspaceController` (already so).
- Don't couple `OutputPanel` to download-only; leave room for a preview consumer of the same text.
- Keep Customizer-structural trivia intact in the default profile (the bundler already does).

**Not in scope now.** Revisit after v1 ships and payload/licensing are assessed.
