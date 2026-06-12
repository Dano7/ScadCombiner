# ScadBundler Live — Documentation Package

**ScadBundler Live** is the browser companion to the ScadBundler CLI: a web page where a non-technical
maker drags their OpenSCAD files in and gets one bundled `.scad` file to upload to sites
(Thingiverse / MakerWorld / Printables) that only accept a single file. It runs the **same proven
`ScadBundler.Core` pipeline** entirely in the browser via **Blazor WebAssembly** — no server, no upload,
files never leave the user's machine.

> This package is **spec-first and one-shot-AI-ready** — the same intent as `docs/slices/`. It is written
> to be handed to an AI coding agent (or several, for comparison) and implemented with no further
> clarification. Read in this order:

| Doc | What it gives you |
|---|---|
| **[Spec.md](Spec.md)** | The product: audience, UX flows, feasibility, the **Core/Workspace facade contract**, behaviors (entry-point inference, missing-reference handling), options↔CLI-flag mapping, non-goals. |
| **[Design.md](Design.md)** | The architecture: Blazor WASM + Core/Workspace split, `InMemoryFileSystem`, the component map + state flow, JS-interop surface, hosting, testing & coverage policy, performance. |
| **[Development-Slices.md](Development-Slices.md)** | The slice index (W0–W4) and build order. |
| **[slices/](slices/)** | The per-slice specs — `Slice-W0` … `Slice-W3` (v1) + `Slice-W4` (deferred preview). Each has explicit Scope (In/Out) and Exit Criteria. |

## The one fact that makes this cheap

`ScadBundler.Core` has **zero external dependencies** and reads every file through a single
[`IFileSystem`](../../src/ScadBundler.Core/Loading/IFileSystem.cs) seam. So the only genuinely new code is:

1. a small **Core/Workspace facade** (`src/ScadBundler.Core/Workspace/`) — an in-memory file system plus
   entry-point inference, a dependency/missing-reference report, and a browser-friendly bundle call; and
2. a **thin Blazor UI shell** (`web/ScadBundler.Web/`) that drives it.

Everything else — loading, parsing, semantic analysis, inlining, hardening, emitting — is reused unchanged.

## Status

**Planning complete; implementation not started.** v1 is bundle-only (no 3D preview — see
[Slice-W4](slices/Slice-W4-Preview-Stretch.md)). Decisions locked with the project owner:
Blazor WebAssembly; defer the openscad-wasm preview to v2; single smart drop zone; hosting chosen at
deploy time.
