# ScadBundler Live — Implementation Kickoff & Per-Slice Protocol

Mission, orientation, the per-slice loop, quality gates, and gotchas for building the **ScadBundler Live**
web companion from the docs in this folder. A fresh session should read this once, then execute slice by
slice. (The full spec is in [Spec.md](Spec.md)/[Design.md](Design.md)/[slices/](slices/) — this file is
the *how we work*, not a re-spec.)

---

## Mission

Implement ScadBundler Live — a **Blazor WebAssembly** UI that runs the existing `ScadBundler.Core` pipeline
**in-browser** so a non-technical maker can drag `.scad` files in and get one bundled file. The
spec/design/slices are already written; your job is to **build them one slice at a time** to the repo's
quality bar, with a clean handoff and commit after each slice. You add **no new compiler logic** — the
only new code is the `Workspace/` facade and a thin Blazor shell.

## Orientation — read before touching code (in this order)

1. `CLAUDE.md` (auto-loaded) — project overview + non-negotiables.
2. [README.md](README.md) → [Spec.md](Spec.md) → [Design.md](Design.md) → [Development-Slices.md](Development-Slices.md).
3. **The slice you're about to build**: `docs/live/slices/Slice-Wn-*.md` — its **Scope (In/Out)** and
   **Exit Criteria** are your contract for the slice.
4. [../Constitution.md](../Constitution.md) — the hard rules (≥95% coverage, zero warnings, minimal deps).
5. [handoff.md](handoff.md) **if it exists** — the running web-impl status from prior slices (you maintain it).
6. The Core types the facade wraps (skim to ground the contract — don't re-derive):
   `src/ScadBundler.Core/Inlining/Bundler.cs` (use the **`IFileSystem` overload**) + `BundleOptions.cs`;
   `Loading/IFileSystem.cs`, `SourceLoader.cs`, `LoadGraph.cs`; `Emitting/Emitter.cs`, `EmitOptions.cs`;
   `src/ScadBundler/BundleCommand.cs` (**mirror its option→`BundleOptions`/`EmitOptions` mapping exactly**);
   `Diagnostics/Diagnostic.cs`, `DiagnosticCode.cs`.

## Baseline (once, before starting)

- Confirm you're on the **implementation branch** (not `main`).
- `dotnet build` (zero warnings — warnings are errors) and `dotnet test` must be **green** before you start
  (~682 tests). If the baseline is red, **stop and report** — do not build on red.

## Build order

**W0 → W1 → W2 → W3.** W4 (openscad-wasm preview) is **deferred — do not build it.** W0 is the keystone
(all logic, browser-free, ≥95% covered); make it rock-solid before any UI slice.

## Per-slice loop (repeat for each slice)

1. Read the slice doc; turn its **Exit Criteria into a tracked checklist** (use the todo/task tool).
2. **Implement to the contract.** Test-first for W0 (pure logic). Reuse the existing pipeline — add no
   compiler logic.
3. **Gate before committing:**
   - `dotnet build` → **zero warnings**.
   - `dotnet test` → **all green** (existing + new).
   - **W0:** `dotnet test --collect:"XPlat Code Coverage"` → **≥95% line coverage on
     `src/ScadBundler.Core/Workspace/`**; the **bundle-parity** test passes **byte-identical** vs the CLI.
   - Every Exit Criterion ticked.
4. **Verify behavior, not just tests:**
   - W0: parity vs CLI on the fixtures.
   - W1–W3: `dotnet run --project web/ScadBundler.Web` (or `dotnet watch`); manually exercise
     upload → bundle → copy/download on a **real multi-file project**
     (`C:\git\dan\SCAD\ForkedHolder.scad` + its libs). The `/verify` or `/run` skill and the in-browser
     preview tooling are fair game here.
5. **Update [handoff.md](handoff.md)** (format below).
6. **Commit** — conventional message, ending with the current model's `Co-Authored-By` trailer
   (e.g. `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`). **No push.** One commit per slice
   (or a few logical commits within it).
7. If context budget remains, continue to the next slice; else **stop cleanly** — tree committed, green,
   and handoff.md says exactly where to resume.

## Quality bars & project-specific gotchas (these are where bugs hide)

- **Core stays dependency-free & WASM-clean.** Add **no** NuGet deps to `ScadBundler.Core`; `Workspace/`
  uses only the BCL. Zip ingestion uses BCL `System.IO.Compression.ZipArchive` (no JS lib).
- **No new `SBxxxx` codes.** Entry-point ambiguity, "still need a file", and basename conflicts are
  **facade model states**, not diagnostics. (Honor the repo's "never invent a code" rule.)
- **Use `Bundler.Bundle(root, options, fs)` — the `IFileSystem` overload**, never the disk overload (it
  pulls `OPENSCADPATH`/disk). `LibraryPaths = []` in the browser sandbox.
- **Bundle parity is sacred.** `WebBundler` output must be **byte-identical** to `Bundler` + `Emitter` for
  the same inputs; mirror `BundleCommand`'s mapping exactly (minify ⇒ `Hardening.Minify` **and**
  `EmitOptions(Minify:true)`; obfuscate ⇒ `Hardening.Obfuscate` + drop ordinary comments, license stays
  sticky; any Error diagnostic ⇒ `Text=""`, `Ok=false`).
- **`InMemoryFileSystem` is dumb / exact-path** (POSIX `/`-rooted; `GetFullPath` normalizes `\`→`/`,
  `.`/`..`, leading `/`). All smart resolution (basename match, layout inference) lives in
  `ProjectAnalyzer` — keep them separate.
- **`ProjectAnalysis`** fields: `EntryPointCandidates, InferredRoot, Root, Tree, Missing, Ambiguous,
  Diagnostics`. `Missing` **excludes** ambiguous refs (0 candidates → `Missing`, 1 → resolved, ≥2 →
  `Ambiguous`). The bundle is produced only when **both** `Missing` and `Ambiguous` are empty.
- **Filter SB4001** out of the facade's `Diagnostics` (it drives the missing-file UI, not the problems
  panel).
- **`DiagnosticDto`** = `(Code, Severity, Message, File, Line, Column)` — **never** serialize
  `Span.File.Text`.
- **Blazor = thin shell**, coverage-exempt (documented exception). Correctness lives in the **facade (W0)**;
  UI gets bUnit smoke tests only.
- **Solution wiring:** add `web/ScadBundler.Web` and `tests/ScadBundler.Web.Tests` to `ScadBundler.sln`.
- **Editorconfig/analyzers:** file-scoped namespaces, `var`-only-when-apparent, no top-level statements,
  XML docs on **every public Core member** (CS1591), watch CA1859/CA1822. `.gitattributes` forces LF.
- **Found a spec gap/ambiguity? Fix the spec too** (the repo's spec-driven rule) — update the relevant
  `docs/live/` doc in the same slice; don't just patch code around it.

## handoff.md format (`docs/live/handoff.md` — one living file, updated each slice)

Mirror the repo's root `handoff.md` voice. Maintain a single source of truth for web-impl status:

- A top **"Next session — start here"** line pointing at the next slice (or "v1 done").
- A **"Slice Wn — done (YYYY-MM-DD)"** section per completed slice: files added, key decisions/deviations,
  test count + coverage, Exit-Criteria status, and anything the next session must know (gotchas, TODOs).
- Keep it concise but complete — a cold session should resume from it with no other context.

## Pause and ask the user when

- A slice doc is **ambiguous or contradicts the Core** — raise it and propose a fix.
- **W3 hosting target** needs deciding (GitHub Pages / Cloudflare Pages / Azure Static Web Apps).
- Anything would **deviate materially** from the slices, or **add a dependency to Core**.

## Don't

Don't push. Don't touch `main`. Don't build W4. Don't add deps to `ScadBundler.Core`. Don't invent
diagnostic codes. Don't gold-plate beyond the slice's Exit Criteria.
