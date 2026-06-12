# Slice W0 — Workspace Facade & In-Memory File System

**Status**: spec ready (not started).
**Project**: `src/ScadBundler.Core/Workspace/` (new files in the existing Core library) +
`tests/ScadBundler.Core.Tests`.
**Depends on**: nothing new — reuses `SourceLoader`, `Bundler`, `Emitter`, `Parser` as-is.
**Read with**: [../Spec.md](../Spec.md) §5–§6 (the contract + algorithms), [../Design.md](../Design.md) §2/§6.

This is the **keystone**: every bit of ScadBundler Live logic lives here, browser-free and covered to the
Constitution's ≥95% bar. W1–W3 are a thin UI over it. Because it is pure logic, it is also the most
directly comparable artifact across competing implementations.

---

## 1. Goal

Expose the finished bundler pipeline to an in-memory, upload-driven caller: build a virtual file system
from uploaded files, infer the entry point, report the dependency tree + what's still missing, and produce
a bundle (text + stats + diagnostics) — all without disk, network, or a browser, and **byte-identical** to
what the CLI produces for the same inputs.

**Non-goals:** any UI; any new compiler behavior; any new `SBxxxx` code; basename magic inside the file
system (that's *layout inference* in `ProjectAnalyzer`, not `InMemoryFileSystem`).

---

## 2. Deliverables

```
src/ScadBundler.Core/Workspace/
  UploadedFile.cs            record (Name, Text)
  InMemoryFileSystem.cs      IFileSystem over a virtual '/'-rooted tree
  ReferenceOrigin.cs         enum { Root, Include, Use, Font }
  DependencyModels.cs        DependencyNode, DependencyTree, MissingReference
  ProjectAnalysis.cs         record (EntryPointCandidates, InferredRoot, Root, Tree, Missing, Diagnostics)
  ProjectAnalyzer.cs         static Analyze(uploads, explicitRoot?) → (InMemoryFileSystem, ProjectAnalysis)
  WebBundleOptions.cs        record (BundleLicenses, Hardening, StripLicense, OnCollision, PreserveComments)
  WebBundler.cs              static Bundle(fs, root, options) → WebBundleResult
  BundleStats.cs             record (FilesInlined, OutputBytes, Renames, DefinitionsRemoved, Normalizations)
  WebBundleResult.cs         record (Text, Ok, Diagnostics, Stats)
  DiagnosticDto.cs           record (Code, Severity, Message, File, Line, Column)
  Workspace.cs               (optional) stateful aggregator for the UI — sugar over the above
```

All types **public**; all records are plain/JSON-serializable (no `SourceSpan`/`SourceFile` leakage — use
`DiagnosticDto`). Every public member needs XML docs (CS1591 is on).

---

## 3. Contract

Implement exactly as [../Spec.md](../Spec.md) §5 specifies. Key signatures (repeated here so this slice is
self-contained):

```csharp
public static (InMemoryFileSystem Fs, ProjectAnalysis Analysis) ProjectAnalyzer.Analyze(
    IReadOnlyList<UploadedFile> uploads, string? explicitRoot = null);

public static WebBundleResult WebBundler.Bundle(
    InMemoryFileSystem fs, string root, WebBundleOptions options);
```

### 3.1 `InMemoryFileSystem` (exact-path, dumb on purpose)

- Stores `canonicalPath → text` in an `Ordinal` dictionary. `AddFile` canonicalizes via `GetFullPath`.
- `GetFullPath`: replace `\`→`/`, ensure a leading `/`, collapse `.`/`..` segments. Deterministic; equal
  inputs canonicalize equally (the loader uses it as the cache/cycle key — Spec/Loader contract).
- `FileExists(p)` = the canonical `p` is a stored file. `DirectoryExists(p)` = some stored file's
  directory chain contains canonical `p` (so `ExistsAsFile` in the loader correctly skips directories).
- `Combine(dir, rel)` / `GetDirectoryName(p)`: POSIX-style on the virtual tree.
- `ReadAllText`: return the stored text; throw `FileNotFoundException` if absent (the loader's `TryRead`
  guards with `FileExists`, so this never fires on the happy path — but keep it honest).

> **Cross-check against the loader:** `SourceLoader.IsAbsolute` treats a leading `/` as absolute, and uses
> `Combine(includerDir, rawPath)` for relative refs — both satisfied by the POSIX virtual scheme. Verify
> `ResolvePath` walks: absolute `/...` → `ExistsAsFile` → `GetFullPath`; relative → `Combine` →
> `ExistsAsFile`. No library paths are supplied (sandbox), so resolution is includer-dir-only.

### 3.2 `ProjectAnalyzer.Analyze`

1. **Build the virtual layout** (Spec §6.3): relative-path uploads placed verbatim under `/proj/`; flat
   uploads placed at `/proj/<name>` then satisfied per-reference by basename to a fixpoint; ambiguities
   left unresolved + flagged. Produce the `InMemoryFileSystem`.
2. **Infer or accept the root** (Spec §6.1): parse each file, build the reference graph, pick in-degree-0
   candidates ordered geometry-first; `InferredRoot` when unambiguous; `explicitRoot` overrides.
3. **Dependency report** (Spec §6.2): `SourceLoader.Load(root, BundleOptions.Default-shaped, fs)` →
   walk the `LoadGraph` into a `DependencyTree`; collect `Missing` from unresolved non-font edges;
   project `Diagnostics` (filter SB4001). When `Root` is null, `Tree` is null and `Missing` is computed
   from the raw reference scan instead.
4. Never throw. Empty `uploads` → empty candidates, null root, empty everything.

### 3.3 `WebBundler.Bundle`

- Map `WebBundleOptions` → `BundleOptions` + `EmitOptions` **exactly** as
  [`BundleCommand`](../../../src/ScadBundler/BundleCommand.cs) does (Spec §5.4): `LibraryPaths = []`;
  `Minify` ⇒ `EmitOptions(Minify: true)` and `Hardening.Minify`; `Obfuscate` ⇒ `Hardening.Obfuscate` +
  `EmitOptions(PreserveComments: false)`; `None` ⇒ `EmitOptions(PreserveComments: options.PreserveComments)`.
- `BundleResult r = Bundler.Bundle(root, bundleOptions, fs);` — **the `IFileSystem` overload** (no env).
- If any `r.Diagnostics` is `Error` → `Text = ""`, `Ok = false` (mirror the CLI exit-1 path). Else
  `Text = Emitter.Emit(r.Bundled, emitOptions)`, `Ok = true`.
- `Stats`: `FilesInlined` = distinct inlined files (as `--verbose` computes from the `LoadGraph`);
  `OutputBytes` = UTF-8 byte length of `Text`; `Renames` = count of `SB5004`; `DefinitionsRemoved` /
  `Normalizations` from the relevant codes (`SB5009` summary if present, else count `SB5001`/`SB5002`).
- Project diagnostics to `DiagnosticDto`.

---

## 4. Scope (In / Out)

**In:** the eleven `Workspace/` types; the three algorithms (Spec §6); the option mapping; the
SB4001-filtering projection; the unit-test suite + bundle-parity anchor.

**Out:** anything Blazor; `OPENSCADPATH`/disk (sandbox has none); new diagnostics; worker threads; the
optional `Workspace` aggregator may be deferred to W1 if the UI shapes it differently.

---

## 5. Test plan (xUnit, in `ScadBundler.Core.Tests/Workspace/`)

Drive everything from in-memory `UploadedFile[]` — no disk.

- **InMemoryFileSystem**: canonicalization (`\`→`/`, `.`/`..`, leading `/`); `FileExists` vs
  `DirectoryExists`; `Combine`/`GetDirectoryName`; round-trips a `SourceLoader.Load` over a 3-file tree.
- **Entry-point inference**: single file → itself; one entry + two libs → the entry; two entries (two
  in-degree-0) → `InferredRoot == null`, both in `EntryPointCandidates`; geometry tie-break (a library
  with only defs vs. a file with a top-level `cube()` call) → the caller-bearing file wins; a 2-file cycle
  → falls back to geometry/all.
- **Dependency tree & missing**: diamond include (one shared file loaded once) reflected in the tree; an
  unresolved `use <missing.scad>` → one `MissingReference` with the right `NeededBy`; a `.ttf` `use` →
  `Origin = Font, Resolved = true`, **not** in `Missing`; SB4001 is **absent** from `Diagnostics`.
- **Layout inference**: flat drop of `main.scad`(`include <sub/lib.scad>`) + `lib.scad` resolves and
  bundles; folder drop with real relative paths resolves verbatim; basename ambiguity (two `lib.scad`s) →
  flagged, not silently mis-bound.
- **Option mapping**: each `WebBundleOptions` shape produces the `BundleOptions`/`EmitOptions` the CLI
  would (assert against `BundleCommand`'s mapping); `Obfuscate` drops ordinary comments but keeps the
  license; `StripLicense` drops it.
- **Bundle parity (the anchor):** for ≥3 representative fixtures (single file; include + use; a
  ForkedHolder-shaped Customizer case), assert `WebBundler.Bundle(...).Text` is **byte-identical** to
  `Emitter.Emit(Bundler.Bundle(diskRoot, equivOptions).Bundled, equivEmit)` using a disk fixture of the
  same content. Run under default, `--minify`, and `--obfuscate`.
- **Error gating**: an input that yields an Error diagnostic (e.g. `--on-collision error` on a real
  collision, or a cycle SB4002) → `Ok == false`, `Text == ""`.
- **Never throws**: empty uploads; a root that references only missing files; malformed source.

---

## 6. Exit criteria

- [ ] Zero-warning build (warnings-as-errors); `dotnet test` green.
- [ ] **≥95% line coverage on `Workspace/`** (Constitution).
- [ ] Entry-point inference covers single / ambiguous / cyclic / geometry-tiebreak.
- [ ] Missing-reference enumeration correct incl. fonts excluded and SB4001 filtered from `Diagnostics`.
- [ ] Layout inference: flat **and** foldered uploads resolve; diamond loads once; ambiguity flagged.
- [ ] **Bundle parity proven byte-identical** to the CLI for the fixtures, across Normal/Minify/Obfuscate.
- [ ] No new `SBxxxx` codes; `ScadBundler.Core` stays dependency-free and WASM-clean.
