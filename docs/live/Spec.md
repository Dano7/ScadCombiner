# ScadBundler Live — Product Spec

**Status**: planning complete; implementation not started.
**Audience of this doc**: the AI/engineer implementing the web companion.
**Companion**: [Design.md](Design.md) (architecture), [Development-Slices.md](Development-Slices.md) (build
order), the CLI docs under [../](../) (the pipeline this reuses).

---

## 1. What it is & who it's for

A static web page that turns a multi-file OpenSCAD project into **one** `.scad` file. The user is a
**non-technical maker** — they designed something parametric (or downloaded one that uses a library) and
hit a wall: Thingiverse / MakerWorld / Printables accept a single file, but their model `include`s or
`use`s other files. ScadBundler Live inlines the whole dependency tree into one self-contained file they
can copy or download.

**Core promise:** drag files in → get a correct single file out, with zero install, zero account, and
**zero upload** — everything runs in the browser (Blazor WebAssembly). Files never leave the machine.

**Jobs to be done**

1. *"I have a folder of `.scad` files — give me the one file to upload."*
2. *"I downloaded a model that needs BOSL2 / a helper file — tell me what's missing and bundle it once I
   add them."*
3. *"Make the bundle smaller / less copy-pasteable"* (minify / obfuscate) *— without stripping the
   library authors' credit.*

**Non-goals (v1):** a 3D preview or Customizer UI (deferred to v2 — [Slice-W4](slices/Slice-W4-Preview-Stretch.md));
editing geometry; accounts/persistence/sharing; a server or API backend; anything that uploads the user's
files anywhere.

---

## 2. Why it's feasible (load-bearing facts about the Core)

The web app adds **no compiler logic** — it drives the finished, tested pipeline through its existing
seams. Each fact below is verified in the current `src/ScadBundler.Core`:

1. **Zero dependencies.** [`ScadBundler.Core.csproj`](../../src/ScadBundler.Core/ScadBundler.Core.csproj)
   references nothing but the BCL → it runs unmodified under Blazor WASM (.NET 10).
2. **All file access goes through `IFileSystem`** ([IFileSystem.cs](../../src/ScadBundler.Core/Loading/IFileSystem.cs)).
   `DiskFileSystem` is one impl; an **in-memory** impl over uploaded files drives the entire pipeline.
   The loader owns all policy (search order, caching, cycle detection) — the FS impl is just path
   manipulation + read.
3. **Bundling never throws.** `Bundler.Bundle(rootPath, options, IFileSystem)`
   ([Bundler.cs](../../src/ScadBundler.Core/Inlining/Bundler.cs)) returns a `BundleResult` (flattened AST +
   diagnostics) for *any* input. **This `IFileSystem` overload does not read disk or `OPENSCADPATH`** — it
   only touches what the caller supplies, which is exactly the browser sandbox model.
4. **Missing references are already a first-class signal.** `SourceLoader`
   ([SourceLoader.cs](../../src/ScadBundler.Core/Loading/SourceLoader.cs)) resolves each `include`/`use`,
   and on failure emits **SB4001** and leaves the edge's `Target == null`, continuing. `LoadGraph`
   ([LoadGraph.cs](../../src/ScadBundler.Core/Loading/LoadGraph.cs)) exposes every loaded file
   (`ByAbsolutePath`) and every edge (`LoadedFile.Includes` / `Uses`, each `{ RawPath, Target? }`). Fonts
   (`.ttf`/`.otf`) are `FontPassthrough` and are **not** "missing".
5. **Diagnostics are display-ready.** `Diagnostic(Code, Severity, Message, Span)`
   ([Diagnostic.cs](../../src/ScadBundler.Core/Diagnostics/Diagnostic.cs)); `Span.Start` is a
   `SourcePosition(Offset, Line, Column)` (1-based line/col) and `Span.File.Path` is the file. Messages are
   already human-facing.
6. **The Emitter is a pure string producer.** `Emitter.Emit(ScadFile, EmitOptions?)` → `string`
   ([Emitter.cs](../../src/ScadBundler.Core/Emitting/Emitter.cs)). Deterministic and idempotent.

**Conclusion:** the only new code is the **Workspace facade** (§5) and the **Blazor shell** (Design.md).

---

## 3. UX flows

### 3.1 Page anatomy (top → bottom)

1. **Shell + blurb (paints immediately).** Title, a one-paragraph "why parametric CAD is great, and why a
   bundler unlocks single-file maker sites" pitch, and a subtle "engine loading…" affordance while the
   WASM runtime streams in the background. The page is interactive for reading instantly; the drop zone
   activates when the runtime is ready (a few seconds, cached thereafter).
2. **The smart drop zone.** One large target: **"Drop all your `.scad` files here (or click to choose)."**
   Accepts multiple files and a dropped folder. This is the only thing the user must do.
3. **File list** (appears after the first drop). The inferred **entry point** is badged at the top
   ("★ main file"); every file in the dependency tree is listed with a status icon:
   - ✓ **loaded** — present and parsed,
   - ⚠ **needed** — referenced but not yet uploaded (rendered as its own drop target, §3.3),
   - ⓕ **font** — a `.ttf`/`.otf` `use`, passed through (informational, never blocks).
   The user can **click another file to make it the main file**, or **replace/edit** the main file
   (§3.4).
4. **Problems panel** (only when there are non-missing diagnostics). Each row: `file : line : col` +
   message + an optional friendly explanation keyed by code. **Missing-reference warnings (SB4001) never
   appear here** — they are the file list's ⚠ rows instead.
5. **Output.** The moment the dependency set is complete (no ⚠ rows), the bundle renders into a read-only
   output area and **Copy** + **Download** enable. A short stats line ("4 files combined · 12.3 KB").
6. **Options** (collapsed expander, §3.5).

### 3.2 The smart single drop zone & entry-point inference

When files arrive, the app builds an in-memory project and **infers the entry point** (the "main" file)
— see §6 for the exact rule. The result drives the file list:

- **Unambiguous** (one obvious entry point): badge it, show its dependency tree, bundle immediately if
  complete.
- **Ambiguous** (several files nobody references): list the candidates and ask the user to pick — "Which
  file is your model? (the others look like libraries)". Picking sets the root.
- **Nothing complete yet**: show what was uploaded and what each file still needs.

The user can always override the inference by clicking a file → "Use as main file".

### 3.3 Missing references

A reference that can't be satisfied from the uploaded set is listed in the file tree as a **⚠ needed**
row showing the path as written (`use <BOSL2/std.scad>`) and **who needs it** ("needed by main.scad").
Each needed row is a **drop target**: dropping the matching file there (or anywhere on the zone) resolves
it, re-analyzes, and — if that completes the tree — produces the bundle. The output stays disabled while
any non-font reference is unresolved, with a clear "still need N file(s)" message. Fonts are never
blockers.

### 3.4 Replacing / editing the main file

Per the vision, the user can swap their entry point: drop a different file, click an existing file to
promote it, or **edit the main file's text inline** in a debounced `<textarea>`. On any change the app
re-analyzes and re-highlights which referenced libraries are now used / unused / still missing, and
re-bundles. (A rich editor — Monaco — is an explicit later enhancement, not v1; a plain textarea keeps
the download small and the UX approachable.)

### 3.5 Options (and how they map to the CLI)

A collapsed **Options** expander. Visible controls are deliberately few; power knobs live under
**Advanced**. Every control re-bundles live and maps **exactly** to an existing CLI flag / `BundleOptions`
field (no new behavior is introduced by the web app):

| UI control | Default | Maps to | Notes |
|---|---|---|---|
| ☐ **Remove provenance banners / license aggregation** | off (keep them) | `BundleLicenses = false` (`--no-bundle-licenses`) | Default keeps attribution — the bundle's audience is a downloader who never sees credit otherwise. |
| ◉ **Normal** / ○ **Minify** / ○ **Obfuscate** (radio) | Normal | `Hardening = None / Minify / Obfuscate` (`--minify` / `--obfuscate`) | Mutually exclusive by construction (radio). Minify also sets `EmitOptions.Minify`. |
| *(tooltip on Minify/Obfuscate)* | — | — | "This isn't about denying credit. The license stays. It just nudges the curious to learn from the **original** sources listed above, not from this flattened copy." |
| **Advanced ▸ Collision strategy** | Auto | `OnCollision` (`--on-collision auto/prefix/error/keep-first/keep-last`) | Auto is correct for almost everyone; exposed for parity. |
| **Advanced ▸ ☐ Strip license under minify/obfuscate** | off | `StripLicense = true` (`--strip-license`) | Only meaningful with a hardening profile. |
| **Advanced ▸ ☐ Keep comments** | on | `EmitOptions.PreserveComments` / `BundleOptions.PreserveComments` (`--[no-]preserve-comments`) | Ignored under minify (comments already dropped). |

The mapping is implemented once, in the facade's `WebBundleOptions` → `BundleOptions` + `EmitOptions`
translation (§5.4), which mirrors [`BundleCommand`](../../src/ScadBundler/BundleCommand.cs) so a Live
bundle equals the CLI's output for the same inputs and flags.

---

## 4. Behavior contract (what "correct" means)

1. **Bundle parity.** For the same files + options, the bundle text produced in the browser is
   **byte-identical** to `scadbundler bundle` on disk. The facade and the CLI share `Bundler` + `Emitter`;
   the only difference is the `IFileSystem`.
2. **Never crash on bad input.** Parse errors, missing files, cycles, and empty drops all surface as UI
   state (problems panel / needed rows / empty state), never an exception. The pipeline guarantees this.
3. **Privacy.** No network call carries file contents. The app is static; the engine is local. (Only the
   app's own assets and the WASM runtime are fetched, once.)
4. **Live.** Every interaction (new file, removed file, edited text, toggled option, changed root)
   re-runs analyze + bundle (debounced ~200 ms for typing) and updates the output.
5. **No new diagnostics.** The web app introduces **no new `SBxxxx` codes**. Entry-point ambiguity and
   "still need a file" are UI/facade states, not compiler diagnostics.

---

## 5. The Core/Workspace facade contract (the keystone)

New area **`src/ScadBundler.Core/Workspace/`** — public, browser-free, JSON-serializable, and unit-tested
to ≥95% by the existing `ScadBundler.Core.Tests` project. This is the reusable **"WASM/JSON API"** the
roadmap promised; the Blazor app is one consumer. Specify and implement it exactly as below (every type it
wraps already exists in Core — cross-checked).

### 5.1 Uploaded input

```csharp
namespace ScadBundler.Core.Workspace;

/// One file the user provided. Name is its relative path when known (folder drop / webkitRelativePath),
/// e.g. "BOSL2/std.scad"; otherwise just the file name, e.g. "main.scad".
public sealed record UploadedFile(string Name, string Text);
```

### 5.2 In-memory file system

```csharp
/// An IFileSystem over an in-memory set of files, addressed by virtual POSIX paths rooted at "/".
/// Pure exact-path semantics — no basename magic (layout inference lives in ProjectAnalyzer, §6.3).
public sealed class InMemoryFileSystem : IFileSystem
{
    public void AddFile(string virtualPath, string text);     // canonicalized on insert
    public void RemoveFile(string virtualPath);
    public bool Contains(string virtualPath);
    public IReadOnlyCollection<string> Files { get; }         // canonical virtual paths

    // IFileSystem — GetFullPath normalizes '\'→'/', collapses '.'/'..', ensures a leading '/'.
    // DirectoryExists(p) is true when any file's directory chain contains p. Combine/GetDirectoryName
    // are POSIX-style. ReadAllText throws FileNotFoundException for an absent path (the loader's TryRead
    // already guards with FileExists, so this is never hit on the happy path).
}
```

This stays trivially testable (it is exact-path); the smart resolution is in the analyzer.

### 5.3 Project analysis (inference + dependency report)

```csharp
public enum ReferenceOrigin { Root, Include, Use, Font }

public sealed record DependencyNode(
    string VirtualPath,                       // resolved file, or the raw path for an unresolved/font ref
    bool IsRoot,
    ReferenceOrigin Origin,
    bool Resolved,                            // false ⇒ a "needed" row
    IReadOnlyList<DependencyNode> Children);  // include/use edges, in source order; empty for leaves

public sealed record DependencyTree(DependencyNode Root);

public sealed record MissingReference(
    string RawPath,                           // the <path> exactly as written
    ReferenceOrigin Origin,                   // Include or Use
    IReadOnlyList<string> NeededBy);          // virtual paths of files that reference it

public sealed record ProjectAnalysis(
    IReadOnlyList<string> EntryPointCandidates, // in-degree-0 files, geometry-first ordered
    string? InferredRoot,                       // best single guess; null when ambiguous or none
    string? Root,                               // the root actually used (explicit override or inferred)
    DependencyTree? Tree,                       // null when Root is null
    IReadOnlyList<MissingReference> Missing,     // distinct unresolved (non-font) references
    IReadOnlyList<DiagnosticDto> Diagnostics);   // parse/semantic problems; SB4001 filtered out

public static class ProjectAnalyzer
{
    /// Build the virtual layout (§6.3), infer or accept the root, and report the dependency tree +
    /// missing references + (SB4001-filtered) diagnostics. Never throws.
    public static (InMemoryFileSystem Fs, ProjectAnalysis Analysis) Analyze(
        IReadOnlyList<UploadedFile> uploads,
        string? explicitRoot = null);
}
```

### 5.4 Bundling

```csharp
public sealed record WebBundleOptions(
    bool BundleLicenses = true,
    HardeningProfile Hardening = HardeningProfile.None,   // existing enum (Inlining)
    bool StripLicense = false,
    CollisionStrategy OnCollision = CollisionStrategy.Auto, // existing enum (Inlining)
    bool PreserveComments = true);

public sealed record BundleStats(
    int FilesInlined, int OutputBytes, int Renames, int DefinitionsRemoved, int Normalizations);

public sealed record WebBundleResult(
    string Text,                              // emitted bundle ("" when blocked by Error diagnostics)
    bool Ok,                                  // no Error-severity diagnostics
    IReadOnlyList<DiagnosticDto> Diagnostics,
    BundleStats Stats);

public static class WebBundler
{
    /// Maps WebBundleOptions → BundleOptions + EmitOptions exactly as BundleCommand does, runs
    /// Bundler.Bundle(root, options, fs) then Emitter.Emit, and projects diagnostics/stats. Never throws.
    public static WebBundleResult Bundle(InMemoryFileSystem fs, string root, WebBundleOptions options);
}
```

**Option mapping (must mirror `BundleCommand`):** `Hardening` of `Minify` ⇒ `EmitOptions(Minify: true)`;
`None` ⇒ `EmitOptions(PreserveComments: options.PreserveComments)`; `Obfuscate` ⇒ keep formatting but
drop ordinary comments (`PreserveComments: false`) — the aggregated license + Customizer fence are sticky
and survive regardless. `LibraryPaths` is empty (browser sandbox — no `OPENSCADPATH`). `Ok` gates `Text`:
when any diagnostic is `Error`, return `Text = ""` and `Ok = false` (the CLI's exit-1 behavior).

### 5.5 Diagnostic projection

```csharp
public sealed record DiagnosticDto(string Code, string Severity, string Message, string File, int Line, int Column);
// from Diagnostic: Severity = d.Severity.ToString(); File = d.Span.File.Path;
// Line = d.Span.Start.Line; Column = d.Span.Start.Column. (Never serialize Span.File.Text.)
```

### 5.6 Optional convenience aggregator

A thin stateful `Workspace` (holds the `InMemoryFileSystem` + last `ProjectAnalysis` + chosen root +
options, exposes `AddOrReplace(UploadedFile)`, `SetRoot(string)`, `Remove(string)`, `Analyze()`,
`Bundle(WebBundleOptions)`) **may** live in `Workspace/` for the UI to wrap, but it is sugar over §5.3/§5.4.
The pure functions are the tested contract; the aggregator is not required for ≥95% coverage.

---

## 6. Algorithms to specify precisely

### 6.1 Entry-point inference

Given the uploaded set, for each `.scad` file: parse with `Parser.Parse`, collect outgoing
`IncludeStatement.RawPath` / `UseStatement.RawPath`, and resolve each to an uploaded file (§6.3). Build a
directed graph *file → referenced file*. Then:

- **Candidates** = files with **in-degree 0** (nobody references them).
- **Order candidates** by: (1) **has top-level geometry** — at least one top-level `ModuleInstantiation`
  that is an actual call (not a pure `module`/`function`/assignment) — descending; (2) number of files
  reachable from it, descending; (3) name, ordinal ascending (stable).
- **`InferredRoot`** = the single best candidate when it is unambiguous: exactly one in-degree-0 file, **or**
  exactly one candidate that "has top-level geometry". Otherwise `null` (the UI asks the user to choose).
- An **explicit root** (user override) always wins and skips inference.
- Edge cases: an all-cycle upload (no in-degree-0) → candidates = the geometry-bearing files, else all
  files; a single file → it is the root.

### 6.2 Dependency tree & missing references

With a chosen `Root`, run `SourceLoader.Load(root, BundleOptions, fs)` and read the `LoadGraph`:

- Walk from `Root`, emitting a `DependencyNode` per file; recurse `Includes`/`Uses` in source order.
- An edge with `Target == null` and not `FontPassthrough` and not a cycle ⇒ `Resolved = false`
  (`Origin = Include|Use`); a `FontPassthrough` edge ⇒ `Origin = Font, Resolved = true` (informational).
- **`Missing`** = the distinct unresolved (non-font) `RawPath`s, each with its `NeededBy` list (the files
  whose edges carry that raw path). De-dup by `(RawPath, Origin)`.
- `Diagnostics` = `graph.Diagnostics` (+ a `SemanticAnalyzer.Analyze` pass if surfacing SB3xxx) projected
  to `DiagnosticDto`, **filtering out SB4001** (`DiagnosticCode.IncludeUseNotFound`).

### 6.3 Layout inference (the analyzer/bundler agreement rule)

So that the **bundle resolves exactly as the analysis predicted**, the analyzer builds the
`InMemoryFileSystem` deterministically:

1. **Relative paths known** (folder drop / `webkitRelativePath`): place each file at `"/proj/" + Name`
   verbatim. References resolve through the normal loader by construction.
2. **Flat drop** (names are bare file names): place each at `"/proj/" + fileName`. Then, for every
   reference that does **not** resolve at its written sub-path, try to satisfy it by **basename match**
   against an uploaded file; if found, *also place that file's content at the referenced sub-path*
   (`"/proj/" + rawPath`, with the same canonical text). Repeat to a fixpoint.
   - This makes `include <BOSL2/std.scad>` resolve to an uploaded `std.scad` **and** makes the real
     `Bundler.Bundle` call resolve the identical file — no basename hack inside the loader, no double-load
     for genuinely distinct files.
   - **Ambiguity** (two uploaded files share a basename a reference needs, or one basename is needed at two
     different sub-paths): pick none automatically; record it as a `MissingReference` whose message notes
     the ambiguity, and let the user resolve it by uploading with the right relative path. (Document this;
     it is rare for hand-assembled maker projects.)

> Implementation note: placing the same content at two virtual paths is intentional and safe — the loader
> caches by canonical absolute path, so the alias is a *distinct* node only when its path genuinely
> differs; identical sub-paths collapse. Keep `InMemoryFileSystem` dumb; all of this lives in the analyzer.

---

## 7. Open questions / deferred (record, don't block)

- **openscad-wasm preview + Customizer** — v2, [Slice-W4](slices/Slice-W4-Preview-Stretch.md).
- **Worker-thread bundling** for BOSL2-scale inputs — perf stretch; v1 is synchronous + debounced.
- **Monaco editor** — enhancement over the v1 textarea.
- **Shareable links / project persistence** — out of scope (privacy-first, no backend).
