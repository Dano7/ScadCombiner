# Handoff — Start Here (post-v1: pipeline complete)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1–6 are complete and committed** — the compiler pipeline `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter` is **closed end-to-end**, and the `scadbundler` CLI runs it and packs as a global tool. There is no "next slice"; remaining work is **post-v1** (see below). This file orients you to the finished state.

---

## ▶ Next session — start here

The attribution work that led this file is **done** (see "Done this session" below). Remaining post-v1
work, pick one:

1. **Obfuscator (`--obfuscate`)** — [Post-Demo-Plan.md](docs/Post-Demo-Plan.md) Item D (**vNext**). A
   thin layer over the always-namespace work: same candidate set + reference rewrite + prologue exemption,
   only the name *generator* changes. Must use **deterministic** ids (a counter), never memory addresses
   (those break goldens/idempotence). Design note: an obfuscated bundle should keep the **license block**
   (legal text must survive) even if the per-section banners are dropped.
2. Broader post-v1: WASM/JSON API + "ScadBundler Live", real-world golden masters (BOSL2/NopSCADlib/
   dotSCAD), the OpenSCAD integration harness (V1–V3), emitter line-length wrapping. See §"Post-v1 work".
   The real-world golden masters now also exercise the attribution pass against genuine library license
   headers (BOSL2 is BSD-2-Clause, NopSCADlib GPL-3.0).

### Done this session

**The attribution pass — license aggregation + provenance banners, default on**
([Post-v1-Plan.md](docs/Post-v1-Plan.md) #2 absorbing #3; scope deliberately grown per user decision —
the goal is maker-community trust in bundled parametric models, so the bundle now *explains itself*).

1. **License aggregation.** New [Attribution.cs](src/ScadBundler.Core/Inlining/Attribution.cs) walks the
   `LoadGraph` in **encounter order** (each file's include/use edges by source offset, DFS, root first)
   collecting every file's **header run** — the leading comments of its first statement (or the EOF
   trivia of a comments-only file), **cut at the first Customizer group marker** `/* [Name] */` so the
   Customizer UI is untouched. Runs are deduped by normalized text and **moved**, not copied (stripped at
   assembly by reference identity): the root's header leads unframed, non-root headers follow in a
   delimited block labeled with the `include <…>`/`use <…>` statement that pulled each file in.
   **SB5007** (Info) fires once when ≥1 non-root header lands; cataloged in [Diagnostics.md](docs/Diagnostics.md).
2. **Provenance banners.** At assembly, a one-line banner
   (`// ======== include <lib.scad> ========`, `(continued)` on re-entry) opens each section where the
   origin file of consecutive emitted statements changes — computed from `Span.File`, which survives
   every rewrite, so attribution is structural, not inferred. Sections are contiguous by construction
   (include splices in place; use-imports are grouped per file).
3. **Default on** (`BundleOptions.BundleLicenses = true`; CLI `--[no-]bundle-licenses`): the audience of
   a bundled file is the *downloader* on Thingiverse/MakerWorld, who never sees CLI flags, and silently
   stripping library authors' license headers was the wrong default. `--minify`/`--no-preserve-comments`
   still drop all annotations (they are ordinary comments; the SB6001 structural round-trip is unaffected).
4. **Tests/goldens:** `AttributionTests` (hoist order, dedup-strips-everywhere, group-marker cutoff,
   banner change-tracking, use-labels, comments-only files via EOF trivia, off-switch, error-strategy
   suppression); corpus **`B-010-license-aggregation`** (include + use + diamond include, fully annotated
   golden); CLI default-on and `--no-bundle-licenses` tests; all multi-file `B-*` goldens re-blessed
   (banners). `Attribution` 100% line coverage.

**Previous session:** cross-`include` mis-bind fix under `prefix` (`ResolvePrefix` redirects include-origin
references to the last include-origin def, LocalScope.cc last-wins) and always-namespace `use` imports by
construction ([ADR 0001](docs/adr/0001-include-use-scoping-and-namespacing.md)) — `include` is a flat
textual merge (never namespaced), `use` is per-file `FileContext` isolation (always namespaced); ground
truth verified at `C:\git\hub\openscad`.

---

## Current state

- **Slices 1–6 done** + **post-demo Items A/B/C** + **post-v1 #1–#4** (the attribution pass — license
  aggregation + provenance banners — landed this session): `dotnet build` zero-warning
  (warnings-as-errors), `dotnet test` green (**570 tests**: 550 in `ScadBundler.Core.Tests`, 20 in
  `ScadBundler.Cli.Tests`). Coverage: `Lexing/`≈98%, `Parsing/`≈99%, `Semantics/` 100%, `Loading/`≈98.8%,
  `Inlining/`≈99.6% (**`Attribution.cs` 100%**), `Emitting/`: `Emitter.cs`≈97%, `EmitOptions.cs` 100%.
- **Post-demo (this session), see [docs/Post-Demo-Plan.md](docs/Post-Demo-Plan.md):**
  - **A — Customizer parameters preserved.** The root file's leading parameter assignments are hoisted to the top of the bundle (verbatim, never renamed) and a synthesized `/* [Hidden] */` fences the rest, so OpenSCAD's Customizer shows the model's real knobs instead of an included library's globals. Verified on `C:\git\dan\SCAD\ForkedHolder.scad`. ([Inliner.cs](src/ScadBundler.Core/Inlining/Inliner.cs); golden `slice5-bundle/B-008`.)
  - **B — OpenSCAD-faithful search paths.** New [OpenScadEnvironment.cs](src/ScadBundler.Core/Loading/OpenScadEnvironment.cs) reconstructs OpenSCAD's `parser_init` order: absolutized `OPENSCADPATH` (empty→CWD) + the per-user library folder. Wired through `Bundler`/`BundleCommand`.
  - **C (`--qualify-all`)** and **D (obfuscator, vNext)** remain scoped but unimplemented.
- Branch is **`Claude_implementation`**. Last feature commit: `feat(emitter): implement Slice 6 — emitter & CLI` (this session).
- **Projects:** `src/ScadBundler.Core` (the library), **`src/ScadBundler`** (the CLI, `PackAsTool` → `scadbundler`), `tests/ScadBundler.Core.Tests`, **`tests/ScadBundler.Cli.Tests`**. All four are in `ScadBundler.sln`.
- **Entry points:** `Bundler.Bundle(rootPath, options)` (disk + `OPENSCADPATH`) → `BundleResult`; `Emitter.Emit(scadFile, EmitOptions?)` → `string`. The CLI wires them in `src/ScadBundler/BundleCommand.cs`.

## What Slice 6 added

- **`Emitting/Emitter.cs`** — a deterministic, idempotent recursive pretty-printer. Numbers/strings via `RawText`; author `ParenthesizedExpression` preserved; **precedence-minimal parens** inserted only around synthesized subtrees (thresholds aligned to `docs/Parser-Planning.md`); leading comments on their own indented lines, trailing comments after two spaces, `BlankLineBefore` → one blank line; `--minify` (drops comments/blank lines/optional whitespace, keeps token-separating spaces via a word-char guard). `Emitter.RoundTripsStructurally` is the internal SB6001 self-check (re-parse + `StructuralKey` compare) used by tests.
- **`Emitting/EmitOptions.cs`** — `IndentWidth`/`IndentStyle`/`BraceStyle`/`MaxLineLength` (advisory)/`Minify`/`PreserveComments`. Defaults lock the goldens.
- **`src/ScadBundler` CLI** — `scadbundler bundle <in> [opts]` with every `docs/UX.md` option (`-o`/`-p`/`--on-collision`/`--bundle-licenses`/`--[no-]preserve-comments`/`--minify`/`--dry-run`/`--diff`/`--verbose`); diagnostics grouped by severity to stderr; exit `0`/`1` (any Error diagnostic)/`2` (bad args).
- **Goldens:** `tests/Corpus/slice5-bundle/*/expected.scad` (B-001..B-007, now exact) and `tests/Corpus/slice6-emit/*` (EM-001 Customizer trivia, precedence, control-flow, comprehensions). Regenerate with `BLESS_EMIT=1`.
- **`SB6001`** added to `DiagnosticCode.cs` (the emitter self-check code; reserved/internal).

## Watch items / known gaps (from the Slice-5 cold review)

- ~~**`BundleOptions.BundleLicenses` is not read by the `Inliner`** (silent no-op).~~ **Resolved (this
  session):** the attribution pass implements it, **default on** — see "Done this session".
  `.PreserveComments` remains honored where it belongs, in the **emitter** (`EmitOptions.PreserveComments`).
- ~~**`include`/`use` leading trivia is dropped on flatten.**~~ **Mostly resolved (this session):** every
  file's *header run* (leading comments of its first statement) is now collected and hoisted. Comments
  above a **non-first** include/use line are still dropped — the documented strict-superset case
  ([Post-v1-Plan.md](docs/Post-v1-Plan.md) #3); revisit only on a concrete need.
- ~~**Latent cross-`include` mis-bind under non-`Auto` strategies**~~ **Resolved (this session):** the failing strategy was **`prefix`** — `NamespaceRep` rewrote cross-`include`-duplicate *references* per-rep via `ISemanticModel.ReferencesTo`, trusting the pre-inline model's per-file binding. `ResolvePrefix` now redirects every include-origin reference to the last include-origin definition (LocalScope.cc last-wins). `Auto`/`keep-first`/`keep-last` were already correct. See [Post-v1-Plan.md](docs/Post-v1-Plan.md) #4.
- ~~**`CollisionStrategy.Error`** emits the same collision *warnings* as `Auto` and returns an empty bundle (no dedicated error-severity code), so the CLI exits `0` with empty output for that mode.~~ **Resolved (post-v1):** a genuine collision under `--on-collision error` now emits **SB5006** (Error-severity, one per colliding site) and the CLI exits `1` with no output. See [docs/Post-v1-Plan.md](docs/Post-v1-Plan.md).

## Post-v1 work (see `docs/Development-Slices.md`)

- **WASM/JSON API + "ScadBundler Live"** web companion (the Core is dependency-free and consumable for this).
- **Real-world golden masters**: small slices of BOSL2 / NopSCADlib / dotSCAD.
- **Integration harness (V1–V3)** against the official OpenSCAD C++ engine (test-only; render-equivalence). Ground truth checkout at `C:\git\hub\openscad`; fixtures in its `examples/` and `tests/data/modulecache-tests/`.
- Line-length wrapping in the emitter (`MaxLineLength` is advisory today). (`--bundle-licenses`
  aggregation is **done** — this session.)

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~EmitterTests"
dotnet test --collect:"XPlat Code Coverage"
dotnet run --project src/ScadBundler -- bundle main.scad -o bundled.scad
dotnet pack src/ScadBundler -c Release            # build the dotnet-tool package
# BLESS_EMIT=1 dotnet test --filter Slice6CorpusTests   # regenerate emitter goldens
```

## Workflow / repo conventions

- Commits on `Claude_implementation`, **conventional commits**, ending with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Commit when a unit is done; don't push unless asked.
- `.gitattributes` forces **LF**; `.editorconfig` enforces file-scoped namespaces, `var`-only-when-apparent, no top-level statements, and warnings-as-errors. Every **public** Core member needs XML docs (CS1591); watch CA1859/CA1822.
- If you find a genuine spec gap/ambiguity, **fix the spec too** (one-shot, spec-driven). Slice 6 locked the keyword-paren spacing rule in `docs/slices/Slice-6-Emitter-CLI.md` §5.
