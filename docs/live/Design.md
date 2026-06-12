# ScadBundler Live — Design (Architecture)

**Status**: planning complete; implementation not started.
**Reads with**: [Spec.md](Spec.md) (product + facade contract), [Development-Slices.md](Development-Slices.md)
(build order). This doc covers *how* it's built: the projects, the component map, state flow, JS interop,
hosting, testing, and performance.

---

## 1. Architecture at a glance

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Static host (GitHub Pages / Cloudflare / Azure SWA — chosen at deploy)    │
│                                                                            │
│  web/ScadBundler.Web   (Blazor WebAssembly, .NET 10)                       │
│    Components (Razor) ── WorkspaceController (DI service, all UI state) ──┐ │
│    minimal JS interop: clipboard · download · drag-drop · folder paths   │ │
│                                                  in-process calls (no HTTP)│ │
│                                                                          ▼ │
│  ScadBundler.Core / Workspace   (NEW — browser-free facade, ≥95% covered)  │
│    InMemoryFileSystem : IFileSystem                                         │
│    ProjectAnalyzer.Analyze(...)   WebBundler.Bundle(...)                    │
│                                                  ▼                         │
│  ScadBundler.Core pipeline (UNCHANGED)                                      │
│    SourceLoader → Parser → SemanticAnalyzer → Inliner → Transformer → Emitter
└──────────────────────────────────────────────────────────────────────────┘
```

The browser downloads the .NET WASM runtime + the trimmed app assemblies **once** (cached). All work —
parse, analyze, bundle, emit — happens locally. No file content ever crosses the network.

---

## 2. Projects & solution layout

| Path | Kind | New? | Notes |
|---|---|---|---|
| `src/ScadBundler.Core/Workspace/` | C# (in the existing Core lib) | **new files** | The facade (Spec §5). No new project; keeps it covered by `ScadBundler.Core.Tests`. |
| `web/ScadBundler.Web/` | Blazor WebAssembly app (.NET 10) | **new project** | The UI shell. References `ScadBundler.Core`. |
| `tests/ScadBundler.Web.Tests/` | bUnit + xUnit | **new project** (W1+) | Thin-shell component smoke tests; **not** held to ≥95%. |
| `ScadBundler.sln` | solution | edit | Add the two new projects. |

**Why the facade lives in Core, not the web app:** the Constitution requires **≥95% line coverage** on the
code that matters. Plain records + static methods are trivially covered by the existing test project, with
no browser. Blazor components are not — so they stay a *thin shell* (explicit coverage exception, §6).
Putting the logic in `Workspace/` also makes it the reusable "WASM/JSON API": a future JS frontend, a
`scadbundler --json` mode, or a server re-skins the same contract.

> **Core hygiene to preserve:** `ScadBundler.Core` must remain dependency-free and WASM-clean — the
> `Workspace/` code uses only BCL types already in use elsewhere (no `System.IO` disk calls; that's what
> `InMemoryFileSystem` replaces). Do not add a NuGet dependency to Core for the web work.

---

## 3. The Blazor shell

### 3.1 Component map

| Component | Responsibility |
|---|---|
| `App.razor` / `MainLayout` | Page chrome; mounts the single-page experience (no routing needed in v1). |
| `Landing` | Title + the "why parametric CAD / why bundle" blurb; renders instantly (static markup). |
| `EngineStatus` | Tiny "engine loading… / ready" indicator bound to runtime-ready state. |
| `DropZone` | The single smart drop/upload target. Wraps `InputFile` for click-to-choose and a JS drag-drop handler for drop (incl. folders). Emits `UploadedFile`s to the controller. |
| `FileList` | The dependency tree: entry-point badge, per-file status icons, click-to-promote; hosts `MissingRow`s. |
| `MissingRow` | A ⚠ "needed" reference rendered as its own drop target ("drop `lib.scad` here"). |
| `MainFileEditor` | Debounced `<textarea>` bound to the current root's text; edits re-analyze + re-bundle. |
| `ProblemsPanel` | Non-missing `DiagnosticDto`s: `file : line : col` + message + friendly per-code text. |
| `OptionsPanel` | The provenance checkbox, the Normal/Minify/Obfuscate radio (+ tooltip), the Advanced sub-section. |
| `OutputPanel` | Read-only bundle text, stats line, **Copy** + **Download** buttons (disabled until complete). |

### 3.2 State — one controller

A single DI-registered **`WorkspaceController`** (scoped singleton) owns *all* mutable state and is the
only thing that calls the facade. Components bind to it and raise events; it never touches the DOM.

```csharp
public sealed class WorkspaceController
{
    // state
    public IReadOnlyList<UploadedFile> Uploads { get; }
    public ProjectAnalysis? Analysis { get; }
    public WebBundleResult? Bundle { get; }
    public WebBundleOptions Options { get; private set; }
    public string? Root { get; private set; }          // user override or inferred
    public event Action? Changed;                       // components subscribe → StateHasChanged

    // intents (each runs Recompute() then fires Changed)
    public void AddOrReplace(IEnumerable<UploadedFile> files);
    public void Remove(string virtualPath);
    public void SetRoot(string virtualPath);
    public void EditMainFile(string newText);           // debounced by the caller (~200 ms)
    public void SetOptions(WebBundleOptions options);

    // Recompute: (fs, Analysis) = ProjectAnalyzer.Analyze(Uploads, Root);
    //            Root = Analysis.Root; if Root != null && Analysis.Missing is empty:
    //                Bundle = WebBundler.Bundle(fs, Root, Options); else Bundle = null.
}
```

`Recompute` is synchronous and fast (Spec §2.7). Typing in `MainFileEditor` is debounced in the component
(a `System.Timers.Timer` / `CancellationToken` delay) so we re-bundle on a pause, not per keystroke.

### 3.3 Data flow (one cycle)

```
user action → component event → WorkspaceController intent
   → ProjectAnalyzer.Analyze(uploads, root)          // build layout, infer root, dependency report
   → if complete: WebBundler.Bundle(fs, root, opts)  // Bundler.Bundle(IFileSystem) + Emitter.Emit
   → Changed → components re-render (file list, problems, output)
```

---

## 4. JavaScript interop (kept minimal)

Blazor handles file reading and rendering; JS is a thin shim only where the browser API has no managed
surface. Keep all of it in one small `wwwroot/interop.js`.

| Need | Mechanism |
|---|---|
| Pick files | `InputFile` (managed; streams file contents — **no JS**). |
| Drag-and-drop **files** | A JS `drop` handler reads `DataTransfer.files`, passes name+text to .NET via `DotNet.invokeMethodAsync`. |
| Drag-and-drop **folders** | Same handler walks `DataTransferItem.webkitGetAsEntry()` to recover **relative paths** (`webkitRelativePath`-equivalent), so layout inference (Spec §6.3) gets real structure. |
| Copy to clipboard | `navigator.clipboard.writeText` via `IJSRuntime`. |
| Download | Build a `Blob`, create an object URL, click a synthetic anchor, revoke — small JS function invoked from `OutputPanel`. |
| Drop-zone styling | CSS `:drag` states + a class toggled by the JS handler. |

No JS UI framework, no bundler toolchain beyond what the .NET SDK provides.

---

## 5. Hosting & deploy (host chosen later)

The output of `dotnet publish web/ScadBundler.Web -c Release` is a **static `wwwroot/`** — deployable to
any static host. Plan generically; pick the host in [Slice-W3](slices/Slice-W3-Options-Polish-Deploy.md):

- **Publish settings:** IL **trimming** on (shrinks the assembly payload), **Brotli** precompression
  (Blazor emits `.br`), invariant globalization where possible (drops ICU weight). AOT is **not** needed —
  the workload is tiny; trimming + Brotli is the right cost/benefit. Measure the payload; target a fast
  first interaction.
- **First paint vs. runtime:** `index.html` renders the shell + blurb immediately; the WASM runtime
  streams behind it; the drop zone enables on `ready`. (Blazor's loading UI is replaced with the branded
  shell.)
- **Host specifics (deferred):** base-href handling for project sub-paths (GitHub Pages), SPA fallback to
  `index.html`, correct `.br`/`.wasm` MIME + long-cache headers, and a CI workflow (GitHub Actions →
  Pages, or the host's Git integration). Decide with the owner at W3.

---

## 6. Testing & coverage policy

- **`Workspace/` facade → ≥95% line coverage** (Constitution bar), in `ScadBundler.Core.Tests`, **no
  browser**. This is where correctness is proven (Spec §6 algorithms, option mapping, bundle parity). The
  pure-data design makes this straightforward.
- **Blazor shell → thin, exempt from ≥95%** (explicit, documented exception). Covered by:
  - a handful of **bUnit** component tests (file-list status rendering, problems-panel filtering of
    SB4001, options→`WebBundleOptions` mapping, output enable/disable gating);
  - **manual acceptance** on a real multi-file project (e.g. `C:\git\dan\SCAD\ForkedHolder.scad` + its
    libs);
  - optional **Playwright** E2E (upload → bundle → download) as a stretch.
- **Bundle-parity test (W0, the anchor):** for representative fixtures, assert
  `WebBundler.Bundle(inMemory, root, opts).Text` equals `Bundler.Bundle(diskRoot, equivalentOpts) →
  Emitter.Emit` **byte-for-byte**. Because both share `Bundler` + `Emitter`, the in-browser output is
  render-equivalent by construction, and the existing OpenSCAD differential harness still vouches for it.
- **Reuse existing test infrastructure:** the Core test project already drives the pipeline through an
  in-memory file system for fixtures — model `InMemoryFileSystem` tests on those patterns.

---

## 7. Performance

- **Synchronous is fine for v1.** Typical maker projects are a handful of small files; analyze+bundle is
  well under a frame. Debounce text edits (~200 ms) so we bundle on pause.
- **Blazor WASM is single-threaded** by default. A BOSL2-scale bundle could block the UI briefly; if that
  ever matters, move `Recompute` to a Web Worker (.NET 10 WASM threading or a JS-worker bridge) — **noted
  as a perf stretch, not v1**.
- **Payload, not CPU, is the cost.** The felt latency is the one-time runtime download; trimming + Brotli
  + caching is the lever (§5). The compute is negligible.

---

## 8. What this design deliberately avoids

- **No server / API / database.** Static + local only (privacy, zero cost, infinite scale).
- **No JS↔WASM JSON bridge.** Blazor calls the C# facade in-process; the `DiagnosticDto`/`ProjectAnalysis`
  records are *also* JSON-serializable so a future non-Blazor frontend could reuse them — but v1 needs no
  serialization across that boundary.
- **No new compiler code.** The web app only orchestrates the finished pipeline; correctness is inherited.
- **No new diagnostic codes** (Spec §4.5).
