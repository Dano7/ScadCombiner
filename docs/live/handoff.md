# Handoff — ScadBundler Live (web-impl status)

Running status of the **ScadBundler Live** web companion build (the docs in this folder; protocol in
[IMPLEMENTATION-KICKOFF.md](IMPLEMENTATION-KICKOFF.md)). One living file, updated per slice. A cold session
should be able to resume from here with no other context.

---

## ▶ Next session — start here

**W0 + W1 + W2 + W3 are done and green.** The web companion is **feature-complete for v1 and shipping**.
**W4 (openscad-wasm 3D preview) stays deferred — do not build** unless the owner asks. There is no further
required web slice; remaining roadmap work is the real-world golden masters (BOSL2/NopSCADlib/dotSCAD) and
post-v1 stretches (worker-thread bundling, Monaco) — none of them blocking.

**One manual step the owner must do once (GitHub Pages):** in the repo's **Settings → Pages → Build and
deployment**, set **Source = "GitHub Actions"**. That's the only switch — no branch, no extra account. The
workflow ([../../.github/workflows/deploy-pages.yml](../../.github/workflows/deploy-pages.yml)) then deploys
on every push to `main` (and via the **Run workflow** button / `workflow_dispatch`). Live URL once enabled:
**<https://dano7.github.io/ScadCombiner/>** (repo `Dano7/ScadCombiner`; the workflow derives the
`/<repo>/` base-href dynamically, so a rename just works). This branch ships as a **feature branch** —
Pages won't deploy until it lands on `main` (or you manually dispatch the workflow from this branch).

> Before adding **any** dependency to `ScadBundler.Core`, stop and ask (Core stays dependency-free).

---

## Slice W3 — done (2026-06-13)

**Options, polish & deploy — the shipping slice.** The bundler's flags are now friendly controls that
re-bundle live and map **exactly** to the CLI; the page is responsive + accessible with clear
empty/incomplete/error/success states; and a GitHub Actions → Pages workflow publishes the trimmed static
build. **Core untouched; no Core dependency added; no new `SBxxxx` codes.** Build 0 warnings; **794 tests
green** (Core 692, CLI 23, Integration 34, **Web 45** [+10]).

> **Post-ship fix (2026-06-13):** `App.razor` wired three **string-typed** child params as literals instead
> of expressions — `MainFileEditor Root`/`RootText` and `OutputPanel RootPath` were `="Controller.Root…"`
> (no `@`), so the editor displayed the text *"Controller.RootText"* and the download name became
> *"Controller.bundled.scad"*. Fixed to `="@Controller.…"`. The isolated component tests set params via the
> typed builder API and never exercised the markup, so the new `AppTests` renders the **full `App`** as a
> guard (verified to fail pre-fix). See the binding gotcha below.

### Files added (`web/ScadBundler.Web/`)
- `Components/OptionsPanel.razor` — a collapsed `<details>` expander (Spec §3.1 item 6). Controls: the
  **provenance** checkbox (ticked ⇒ `BundleLicenses=false`, the inverse — default keeps attribution), the
  **Normal/Minify/Obfuscate** radio (`name="hardening"`, mutually exclusive by the single `Hardening`
  enum) with the verbatim "credit stays" note shown when a profile is picked, and an **Advanced**
  sub-`<details>` (collision `<select>` bound via `value=`/`@onchange`; **strip-license** checkbox disabled
  unless a profile is selected; **keep-comments** checkbox disabled under Minify). Every handler emits a
  `Opts with { … }` copy through `Controller.SetOptions` → live re-bundle. Injects `WorkspaceController`;
  re-renders via App's `Changed` cascade (no own subscription needed). Wired into `App.razor` (gated on
  `Analysis != null`, after `OutputPanel`).

### Files added (deploy / docs)
- `.github/workflows/deploy-pages.yml` — on push to `main` (+ `workflow_dispatch`): checkout → setup .NET
  10 → `dotnet workload install wasm-tools` → `dotnet publish -c Release` → **sed-rewrite `<base href>`**
  from `"/"` to `/${{ github.event.repository.name }}/` in the published `index.html` → `cp index.html
  404.html` (SPA fallback) + `touch .nojekyll` → `upload-pages-artifact` → `deploy-pages`. Permissions
  `pages: write` + `id-token: write`; `concurrency: pages`.

### Files changed
- `Components/OutputPanel.razor` — **error state**: when `Result.Ok` is false the panel shows a
  `role="alert"` notice ("…must be fixed…see Problems above") instead of the textarea/buttons; the emit
  controls + stats render only when `CanEmit` (Ok && non-empty). **Stats line** now appends `· N renamed`
  / `· M removed` when a hardening profile ran (`BundleStats.Renames`/`DefinitionsRemoved`).
- `Components/FileList.razor`, `ProblemsPanel.razor` — `role="img" aria-label=…` on the ✓/○/ⓕ status and
  severity icons; `aria-label` region on the problems section.
- `Components/DropZone.razor` — `role="group" aria-label` (the Choose folder/files buttons are the
  keyboard path; drag-drop is the enhancement).
- `wwwroot/css/app.css` — options-panel styles, `.output-error`, a global `:focus-visible` outline, and a
  `@media (max-width: 640px)` single-column reflow (stacked drop-zone actions, wrapped headers/actions).
- `docs/README.md` — companion link now points at the live URL + an "or use the web version" pointer.

### Quality / verification
- **Build: 0 warnings. Tests: 793 green.** New: `OptionsPanelTests` (8 — provenance inversion, radio
  mutual-exclusion, strip-license enable-gating, keep-comments disable-under-minify, collision select,
  live re-bundle) + 1 `OutputPanelTests` (renamed/removed stats; the two old "disabled buttons" tests
  became "error notice / no buttons" for the new contract) + `BundleParityTests.OptionPermutations` (10
  knob combinations, all byte-identical to the disk/CLI mapping).
- **Release publish verified locally:** trimmed + invariant-globalization, Brotli **and** gzip emitted
  (41 each), ~2.3 MB compressed runtime; published `index.html` carries `<base href="/" />` (so the sed
  matches). The workflow's post-publish steps were **simulated locally** (sed → `/ScadCombiner/`, 404
  copy, `.nojekyll`, `_framework/` intact, zero stray `href="/"`).
- **App boots:** `dotnet run … --urls http://localhost:5219` serves `/`, `_framework/blazor.webassembly.js`,
  `interop.js`, `css/app.css` (all 200); `#app` shell + the new `.options-body` CSS present.

### Gotchas the next session must know
- **`OptionsPanel` reads `Controller.Options` directly** (no `[Parameter]`) and re-renders only because
  App re-renders on `Changed`. In a **standalone bUnit** test it still re-renders after its *own* control
  event (Blazor re-renders a component after handling its event), which is why the enable/disable-gating
  assertions work without an App host — but if you render it under a different parent that suppresses
  cascade, subscribe to `Changed` like `App` does.
- **`keep-comments` is effectively a Normal-mode knob.** The emit mapping sets
  `EmitOptions.PreserveComments = Hardening==None && PreserveComments`, so under Minify/Obfuscate the
  toggle changes `BundleOptions.PreserveComments` but the emitted bytes are unchanged (comments already
  dropped at emit). It's disabled under Minify in the UI; left enabled-but-inert under Obfuscate to match
  the CLI (which accepts `--no-preserve-comments --obfuscate`). Parity holds either way.
- **The `.NET 10 SDK on CI`**: the workflow pins `dotnet-version: '10.0.x'` and installs the `wasm-tools`
  workload before publish. If a future runner lacks 10.0 GA, switch to a `global.json` pin or add
  `dotnet-quality: preview` — don't silently let it fall back to an older SDK (the project is net10.0).
- **Deploy is gated on the one Settings switch** (Source = GitHub Actions). Until the owner flips it, the
  workflow run will fail at the `deploy-pages` step with a "Pages not enabled" error — that's expected,
  not a workflow bug.
- **`@` is mandatory on string-typed component params in `.razor` markup.** Razor evaluates a bare
  `Attr="expr"` as a C# expression only for **non-string** params; for a `string`/`string?` param it's a
  **literal**, so `RootText="Controller.RootText"` ships the text, not the value (the post-ship fix above).
  Non-string params (`Analysis`, `Uploads`, `Result`, `Diagnostics`) are fine without `@`. bUnit component
  tests set params via the builder API and **cannot** catch this — render the full `App` (`AppTests`) to
  guard markup wiring.

---

## Slice W2 — done (2026-06-12)

**Dependency UX & friendly errors.** The page is now smart and forgiving: missing libraries are listed
(with "needed by") as drop targets, the entry point can be picked/re-designated, the main file is editable
inline with live re-analysis, used/unused files are highlighted, a friendly problems panel explains real
syntax/semantic issues (never SB4001), a read-only structure tree shows the resolved layout, and basename
conflicts get a one-click picker. **Core untouched; no Core dependency added; no new `SBxxxx` codes.**

### Controller intents added (`web/ScadBundler.Web/State/WorkspaceController.cs`)
- `EditMainFile(newText)` — replaces the **current root's** upload text in place (maps `Root`’s canonical
  `/proj/…` path back to the keyed `UploadedFile.Name`) and re-analyzes. No-op without a root.
- `ResolveAmbiguous(candidateVirtualPath, asPath)` — re-adds the picked candidate's content under `asPath`
  via the existing `AddOrReplace` (no new facade call), clearing the ambiguity next analysis.
- `RootText` (get) — the current root's text, the `MainFileEditor` seed. `TextForVirtualPath(path)` — a
  candidate's text for the picker's size/snippet. Both use one private `NameForCanonical` reverse-map.
- These are **controller** conveniences, not new facade methods (Design §3.2 updated to list them).

### Files added (`web/ScadBundler.Web/`)
- `State/FileClassifier.cs` — pure `Classify(uploads, analysis) → ClassifiedFile[]` partition into
  **Root / Used / Unused** (`FileUsage`). Used = resolved tree paths; matched exact **or case-insensitively
  on the full path** so a basename-aliased file (e.g. uploaded `MyLib.scad` referenced `<mylib.scad>`)
  still counts as used. Unit-tested directly (no browser).
- `Components/FriendlyDiagnostics.cs` — the UI-only `SBnnnn → one sentence` map (Slice §3); unknown code ⇒
  no extra line (raw message only).
- `Components/ProblemsPanel.razor` — non-missing diagnostics grouped Error→Warning→Info, each
  `file : line : col` + message + friendly line; **re-filters SB4001 defensively**.
- `Components/MissingRow.razor` — one ⚠ row per `Missing` (RawPath + "needed by" + a drop hint). It is its
  own drop target via `scadLive.registerDropZone(element)`, which dispatches to the **global** DropZone
  `.NET` ref (set in `scadLive.init`) → `AddOrReplace` → re-analyze. No per-row `[JSInvokable]`; listeners
  die with the element (no unregister needed).
- `Components/StructureTree.razor` — **read-only** folder tree built from the upload `Name`s (recursive
  `RenderFragment`). No inputs/buttons (asserted in tests) — structure comes from the upload, never edited.
- `Components/ConflictPicker.razor` — per `AmbiguousReference`: radio-selectable candidate cards
  (size · first-line snippet) + a "Use this file" button (place at the written path) + an inline
  "…or place it at:" field (place the selected candidate at a typed sub-path). Both call
  `Controller.ResolveAmbiguous`.
- `Components/MainFileEditor.razor` — debounced `<textarea>` (params `Root`, `RootText`, `DebounceMs`=200;
  `CancellationTokenSource` + `Task.Delay`). Reloads only when the root **file** changes (param compare in
  `OnParametersSet`), so re-analysis never clobbers in-progress typing.

### Files changed
- `Components/FileList.razor` — **reworked**: now injects `WorkspaceController` (for `SetRoot`) and takes
  `Analysis` + `Uploads` params. Shows the entry-point candidate picker when `Root` is null ("Which file is
  your model?"), the **classified** file rows (★ main badge on the root; a "★ make main" link on every
  other row; ✓ used / ○ unused with an "unused" tag), font ⓕ rows, and hosts `MissingRow` + `ConflictPicker`
  under "Still needed".
- `App.razor` — composes `StructureTree` → `FileList` → `MainFileEditor` → still-need line → `ProblemsPanel`
  → `OutputPanel`; passes `Root`/`RootText`/`Uploads`; diagnostics source = `Bundle?.Diagnostics ??
  Analysis?.Diagnostics`.
- `wwwroot/css/app.css` — styles for unused rows, make-main links, drop-target rows, conflict picker,
  structure tree, editor, problems panel.

### Quality / verification
- **Build: 0 warnings.** **Tests: 783 green** (Core 691, CLI 23, Integration 34, **Web 35** [+20]).
- New web tests: `FileClassifierTests` (pure), `ProblemsPanelTests`, `MissingRow`/re-root/unused/ambiguous
  in `FileListTests`, `MainFileEditorTests`, `ConflictPickerTests`, `StructureTreeTests`, and
  `WorkspaceControllerTests` (`RootText`/`EditMainFile`/`ResolveAmbiguous`). All driven by the **real**
  `ProjectAnalyzer`, so rendering matches the live app.
- **App boots:** `dotnet run --project web/ScadBundler.Web --urls http://localhost:5219` serves the shell +
  `_framework/blazor.webassembly.js` + `interop.js` + `ScadBundler.Web.wasm` (all 200).

### Gotchas the next session must know
- **`MainFileEditor` debounce is a real timer.** Its bUnit test uses `DebounceMs:1` + a 10 s
  `WaitForAssertion` window because this assembly can run alongside the CPU-heavy OpenSCAD integration
  suite; a tighter window flaked once under that contention. Keep the generous window if you touch it.
- **`WorkspaceController` dedupes uploads by `Name`** (a `Dictionary`), so two *bare* same-name loose files
  can't coexist there — they collapse (last wins) before any ambiguity arises. `AmbiguousReference` is thus
  reachable via the controller only from **distinct-Name, same-basename** uploads (e.g. `extra/utils.scad`
  + `helpers/utils.scad` with a bare `<utils.scad>` reference) — which is what `ConflictPickerTests` and
  the W2 `WorkspaceControllerTests` use. (The W0 facade still handles the raw two-`UploadedFile` list.)
- **Used/unused classification limitation:** a loose upload aliased to a **different sub-path** (e.g.
  `std.scad` referenced `<BOSL2/std.scad>`) is matched used only by exact/ci full path, so it can show as
  "unused" even though its content is inlined. Rare (folder/zip uploads place verbatim, no alias); revisit
  only if it bites. The bundle itself is unaffected.
- **`MissingRow` reuses the global DropZone ref** — it has no `[JSInvokable]` of its own. If you ever make
  drops on a missing row resolve a *specific* reference (rather than re-analyze-everything), you'll need a
  per-row callback; today "drop anywhere → basename inference resolves it" is intentional.

---

## Slice W1 — done (2026-06-12)

The **Blazor WebAssembly shell + happy-path bundle MVP**: drop a complete multi-file project (folder /
loose files / `.zip`) and get a copyable, downloadable single file — wired entirely to the W0 facade,
**byte-identical to the CLI**. Core untouched; no Core dependency added.

### Projects added (both wired into `ScadBundler.sln`)
- **`web/ScadBundler.Web`** — `Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`, refs `ScadBundler.Core`
  only. `PublishTrimmed` + `InvariantGlobalization` on (Design §5). Explicit `Program.Main` (no top-level
  statements) registers `WorkspaceController` (scoped). `GenerateDocumentationFile=false` (thin shell, not
  a library surface). Packages: `Microsoft.AspNetCore.Components.WebAssembly` (+ `.DevServer`,
  `PrivateAssets=all`).
- **`tests/ScadBundler.Web.Tests`** — `bunit.web` 1.40.0 + xUnit; refs the web app. **15 tests.**

### Files of note (`web/ScadBundler.Web`)
- `State/WorkspaceController.cs` — the single state owner (Design §3.2): `AddOrReplace`/`Remove`/`SetRoot`/
  `SetOptions` → `Recompute()` = `ProjectAnalyzer.Analyze(Uploads, root)` → gate on `Root != null` &&
  `Missing`+`Ambiguous` empty → `WebBundler.Bundle`. Fires `Changed`. (`EditMainFile` deferred to W2.)
- `Ingestion/ZipIngestion.cs` — BCL `ZipArchive` → `UploadedFile`s (`.scad` only, dirs skipped, paths
  preserved). `Ingestion/IngestItem.cs` + `IngestItemReader` — the managed boundary the JS hands files to
  (`text` verbatim, `zip` Base64-decoded + expanded; malformed items skipped, never thrown).
- `Components/`: `Landing` (static blurb), `EngineStatus`, `DropZone` (+ `[JSInvokable] Ingest`), `FileList`
  (★ badge, ✓/⚠/ⓕ icons, "still needed" rows), `OutputPanel` (Copy/Download gated on `Ok && Text>0`,
  download named `<rootstem>.bundled.scad`). `App.razor` composes them + subscribes to `Changed`.
- `wwwroot/index.html` (branded `#app` shell paints **before** the runtime), `wwwroot/interop.js`,
  `wwwroot/css/app.css`.

### Key decisions / deviations (spec edits made this slice)
1. **Unified JS ingestion** instead of the original "pick files = managed `InputFile`" sketch. Blazor's
   `InputFile`/`IBrowserFile` does **not** expose `webkitRelativePath`, so folder picks *require* a JS
   shim anyway; rather than split the path, **all** picking + dropping go through one `interop.js`
   (programmatic hidden `<input>` for picks; the `webkitGetAsEntry`/`readEntries` walk for drops). JS reads
   `.scad` to text and `.zip` to Base64, then calls one `[JSInvokable] DropZone.Ingest(IngestItem[])`;
   unzipping is **managed** (BCL). Still **no JS library**, facade unchanged. → **Design §4 updated** (table
   + a "W1 implementation note" with the trade-off: file text crosses the JS↔WASM boundary as a string —
   negligible at maker scale).
2. **`EngineStatus` "loading" lives in `index.html`, not the component.** In a WASM app the runtime is
   ready by the time any component renders, so the Blazor `EngineStatus` always shows "ready"; the
   pre-boot "Engine loading…" is the static `#app` shell that Blazor replaces. Satisfies "paint shell
   before runtime."
3. **No Core `Workspace` aggregator added** (Spec §5.6). The web `WorkspaceController` plays that role over
   the pure facade functions; the optional Core aggregator stays unbuilt (not needed for anything).

### Quality / verification
- **Build: 0 warnings.** **Tests: 763 green** (Core 691, CLI 23, Integration 34, **Web 15** [+15]).
- **Real-world byte-identical parity re-proven** on `C:\git\dan\SCAD\ForkedHolder.scad` + its 4 libs via a
  *throwaway* loose-upload test (deleted after): facade output == disk/CLI output, **21 845 bytes** each,
  `Missing=0 Ambiguous=0`, `FilesInlined=4`. This exercises the case-insensitive basename inference
  (`include <forkedholderlib.scad>` → `ForkedHolderLib.scad`, `<cleatarray.scad>` → `CleatArray.scad`) in
  the hardest (structure-less) mode.
- **App boots:** `dotnet run --project web/ScadBundler.Web --urls http://localhost:5219` serves the shell
  + `_framework/blazor.webassembly.js` + `interop.js` (all 200); the WASM runtime boots with **no console
  errors**; the branded shell paints before boot.

### Gotchas the next session must know
- **`webkitGetAsEntry()` must be called synchronously** on the `DataTransferItem`s *before* any `await`
  (the items list is emptied after the handler yields) — `interop.js` snapshots entries first. If you add
  more drop handling in W2, preserve that ordering.
- **`MissingRow` drop targets (W2)** can reuse the same `scadLive.registerDropZone` machinery, or just let
  drops anywhere on the main zone resolve them (the facade re-analyzes regardless of where the file landed).
- **Manual run / preview**: the dev server is `dotnet run --project web/ScadBundler.Web --urls
  http://localhost:5219`. (A `.claude/launch.json` was used transiently for the preview tool and removed;
  recreate it if you want the managed preview again.) The preview tool's **screenshot timed out** twice
  this session even though the app booted cleanly (console-verified) — prefer `preview_console_logs` /
  curl over screenshots if it hangs.
- **bUnit version**: pinned `bunit.web` 1.40.0 (classic `Bunit.TestContext` API). The `bunit` 2.x
  metapackage exists (2.7.2) but renames the context type — don't "upgrade" it casually.

---

## Slice W0 — done (2026-06-12)

The browser-free **Core/Workspace facade** — the "WASM/JSON API" the roadmap promised. All logic
(entry-point inference, dependency/missing report, layout inference, bundling) lives here, covered to the
Constitution bar with **byte-identical CLI parity**. No new compiler logic; no new `SBxxxx` codes; Core
stays dependency-free.

### Files added (`src/ScadBundler.Core/Workspace/`)
- `UploadedFile.cs`, `ReferenceOrigin.cs`, `DiagnosticDto.cs`, `DependencyModels.cs`
  (`DependencyNode`/`DependencyTree`/`MissingReference`/`AmbiguousReference`), `ProjectAnalysis.cs`,
  `WebBundleOptions.cs`, `BundleStats.cs`, `WebBundleResult.cs` — the plain, JSON-serializable DTOs.
- `InMemoryFileSystem.cs` — `IFileSystem` over a virtual `/`-rooted tree; **dumb / exact-path / Ordinal**
  (`AddFile`/`RemoveFile`/`Contains`/`Files`). All smart resolution lives in the analyzer.
- `ProjectAnalyzer.cs` — `Analyze(uploads, explicitRoot?)`: layout (basename) inference, entry-point
  inference, dependency tree, missing/ambiguous, SB4001-filtered diagnostics. Never throws.
- `WebBundler.cs` — `Bundle(fs, root, options)`: mirrors `BundleCommand`'s option mapping, runs
  `Bundler.Bundle(root, opts, fs)` (**IFileSystem overload**) + `Emitter.Emit`, projects diagnostics +
  stats. Error-gates `Text=""`/`Ok=false`.

### Tests added (`tests/ScadBundler.Core.Tests/Workspace/`)
`InMemoryFileSystemTests` · `ProjectAnalyzerTests` · `WebBundlerTests` · `BundleParityTests`
(disk-fixture parity across Normal/Minify/Obfuscate + no-license/strip).

### Quality
- **Build: 0 warnings.** **Tests: 748 green** (Core **691** [+61], CLI 23, Integration 34) — baseline was 687.
- **Coverage on `Workspace/`: 98.99% line** (≥95% bar). The few uncovered lines are defensive guards
  (`ReachableCount` no-edge `continue`, `ResolveRef` empty-path, a `foreach` brace in `WebBundler`).
- **Bundle parity proven byte-identical** to the same `Bundler`+`Emitter` over a real temp-dir disk
  fixture, across all three profiles (`BundleParityTests`).

### Key decisions / deviations (and the spec edits that record them)
1. **Basename matching is case-insensitive (`OrdinalIgnoreCase`)** in the analyzer — makers reference with
   sloppy case (ForkedHolder). The alias is still placed at the *exact* loader-resolved path, so the
   bundle resolves precisely what the analysis predicted; `InMemoryFileSystem` itself stays exact/Ordinal.
   → **Spec §6.3 updated.**
2. **Absolute references** can't be satisfied by basename placement (the alias would need an absolute
   home) → reported in `Missing` rather than silently dropped. `ClassifyUnresolved` now sends any
   unresolved-reaching-classification with <2 candidates to `Missing`. → **Spec §6.3 updated.**
3. Aliases are placed at `Combine(includerDir, rawPath)` (canonicalized) — the general form of the spec's
   `"/proj/" + rawPath`, identical when the includer sits at the project root. → **Spec §6.3 clarified.**
4. **Path identity in projections:** the **root** file's `DiagnosticDto.File` and `DependencyNode.VirtualPath`
   are the canonical `/proj/...` path; **included** files keep the loader's display path = the *raw include
   path* (e.g. `lib.scad`). `EntryPointCandidates`/`Root`/`InferredRoot` are all canonical `/proj/...`.
5. `ProjectAnalysis.Diagnostics` = loader diagnostics **+ a `SemanticAnalyzer.Analyze` pass** (surfaces
   SB3xxx), SB4001-filtered, source-ordered. When `Root` is `null`, `Diagnostics` is empty and `Missing`
   comes from a raw reference scan.
6. **Stats:** `Renames` = count(SB5004); `Normalizations` = count(SB5001)+count(SB5002); `DefinitionsRemoved`
   = the tree-shaken count parsed from the **SB5009** summary message (its only public surface; 0 when no
   profile ran); `FilesInlined` = distinct non-root files in the load graph (as `--verbose`); `OutputBytes`
   = UTF-8 length of `Text`.
7. The optional **`Workspace` aggregator deferred to W1** (see Next session).

### Exit criteria — all met
- [x] Zero-warning build; `dotnet test` green.
- [x] ≥95% line coverage on `Workspace/` (98.99%).
- [x] Entry-point inference: single / ambiguous / cyclic / geometry-tiebreak.
- [x] Missing enumeration correct incl. fonts excluded and SB4001 filtered from `Diagnostics`.
- [x] Layout inference: flat **and** foldered/zip resolve; diamond loads once; basename ambiguity surfaces
      as `AmbiguousReference` with its candidate set.
- [x] Bundle parity byte-identical to the CLI across Normal/Minify/Obfuscate.
- [x] No new `SBxxxx` codes; Core stays dependency-free and WASM-clean.

### Gotchas the next session must know
- `WebBundler` double-loads (one `SourceLoader.Load` for `FilesInlined`, plus `Bundler.Bundle`'s internal
  load) — fine for maker-scale inputs; revisit only if BOSL2-scale perf matters (a documented stretch).
- The dependency tree expands a diamond's shared file under **each** parent (it is a tree, not a DAG view);
  the load graph still loads it once (`Stats.FilesInlined` reflects the once-count). Cycle back-edges are
  emitted as resolved leaves (the loader nulls true-cycle targets, so the walk can't recurse forever).
- Coverage check: `dotnet test … --collect:"XPlat Code Coverage"` then filter the cobertura `class`
  entries by `filename -match 'Workspace'` (the PS snippet used this session).
