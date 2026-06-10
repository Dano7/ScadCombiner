# Handoff — Start Here (post-v1: pipeline complete)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1–6 are complete and committed** — the compiler pipeline `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter` is **closed end-to-end**, and the `scadbundler` CLI runs it and packs as a global tool. There is no "next slice"; remaining work is **post-v1** (see below). This file orients you to the finished state.

---

## ▶ Next session — start here

The **OpenSCAD integration harness is done and green** (this session — see below), as are the attribution
work, the Customizer computed-params fix, and the last-wins position fix. Remaining post-v1 work, pick one:

1. **Obfuscator (`--obfuscate`)** — [Post-Demo-Plan.md](docs/Post-Demo-Plan.md) Item D (**vNext**). A
   thin layer over the always-namespace work: same candidate set + reference rewrite + prologue exemption,
   only the name *generator* changes. Must use **deterministic** ids (a counter), never memory addresses
   (those break goldens/idempotence). Design note: an obfuscated bundle should keep the **license block**
   (legal text must survive) even if the per-section banners are dropped.
2. Broader post-v1: WASM/JSON API + "ScadBundler Live", real-world golden masters (BOSL2/NopSCADlib/
   dotSCAD), emitter line-length wrapping. See §"Post-v1 work". The real-world golden masters now also
   exercise the attribution pass against genuine library license headers (BOSL2 is BSD-2-Clause,
   NopSCADlib GPL-3.0) — and can ride the new differential harness for render equivalence.
3. **Small focused follow-up:** [ForwardReferenceChecker.cs](src/ScadBundler.Core/Inlining/ForwardReferenceChecker.cs)
   line coverage is ≈60% (see watch items) — bring it to the ≥95% bar.

### Done this session (2026-06-10, session 3) — OpenSCAD integration harness; it caught a PrivateConstants bug

**1. The integration harness (V1–V3) exists and runs on every `dotnet test`** when OpenSCAD is present:
new **`tests/ScadBundler.IntegrationTests`** project (5th in the sln) wrapping the proven differential
recipe in-process — render the original root to CSG (`openscad.com -o`), bundle via
`Bundler.Bundle` + `Emitter.Emit` (exactly the CLI's wiring), render the bundle **from an
otherwise-empty temp dir** (which also proves self-containment), then assert (a) **no new
warning-class stderr** (`WARNING:`/`DEPRECATED:`/`ERROR:`/`TRACE:`; `in file …, line N` stripped;
multiset, so deprecations may *disappear* but never appear), (b) **identical `ECHO:` output** (ordered),
(c) **byte-identical `.csg`**. Failures keep all artifacts (bundle text, both CSGs/stderrs, bundle
diagnostics) under `%TEMP%\ScadBundlerIntegration\…`; successes clean up.

- **Gating:** `[OpenScadFact]` / `[OpenScadTheory(IntegrationRequirements.…)]` set xunit `Skip` at
  discovery when a prerequisite is missing: the binary (`OPENSCAD_EXE`, else probe
  `C:\Program Files\OpenSCAD\openscad.com` — always the `.com` console wrapper, never the GUI `.exe`),
  the ground-truth checkout (`SCADBUNDLER_OPENSCAD_CHECKOUT`, default `C:\git\hub\openscad`), the real
  projects (`SCADBUNDLER_REAL_PROJECTS`, default `C:\git\dan\SCAD`). `SCADBUNDLER_SKIP_INTEGRATION=1`
  skips all. CI without OpenSCAD skips cleanly; `DifferentialAssert` has a `compareGeometry: false`
  switch for future nondeterministic fixtures (`rands()`/`$t`).
- **Suites (32 tests):** `VerificationBacklogTests` — **V1/V2/V3 all differentially verified** on new
  checked-in fixtures `tests/Corpus/integration/V-001-child-children|V-002-use-scoping|V-003-assign-let`
  (2021.01 still *evaluates* `child()`/`assign()`, emitting `DEPRECATED:` lines the bundle sheds; V-001
  gives `first_only()` two children so `child()` ≡ `children(0)` is disambiguated from "all").
  `ModuleCacheCorpusTests` — 8 positive `modulecache-tests` roots (error-path fixtures excluded by
  design — SB4002 makes cycles hard errors where OpenSCAD tolerates self-`use`; `use-mcad` needs MCAD).
  `ExampleCorpusTests` — 3 single-file `examples/` roots (parse→emit fidelity differential).
  `RealProjectTests` — ForkedHolder, HexContainer, CleatArray, grow-tent-fan-mount, goews (all
  deterministic; `*-combined`/`*.bundled` siblings are artifacts, not roots). `OpenScadStderrTests` —
  binary-free unit tests of the stderr normalization (run in any environment).

**2. The harness's first full run caught a real bug.** `includefrommodule.scad` (official fixture)
bundled to a file that renders `cylinder(r=1)` instead of `r=5` — **silently**: zero stderr both sides
(2021.01 doesn't even warn on the undef read); only the CSG byte-compare sees it. Root cause: a `use`d
file's **private constants were collected from its textual file only**, but its definitions evaluate in
its **FileContext = its include-merge** (`ScopeContext.cc`) — `use <modulewithinclude.scad>` where that
file does `include <radius.scad>` and `mymodule` reads `RADIUS` dropped `RADIUS = 5;` entirely. Fix:

- [SemanticAnalyzer.cs](src/ScadBundler.Core/Semantics/SemanticAnalyzer.cs): reachability edges are
  recorded per **include closure** (`_includeClosures` built between the passes;
  `IsInCurrentFileContext`), not per textual file; `_ownFileReferences` renamed `_fileContextReferences`.
- [ISemanticModel.cs](src/ScadBundler.Core/Semantics/ISemanticModel.cs) / [SemanticModel.cs](src/ScadBundler.Core/Semantics/SemanticModel.cs):
  new closure overload **`PrivateConstants(IReadOnlyList<SourceFile>)`** — seeds from every closure
  file's exported callables and collects reached constants wherever in the closure they are declared;
  the single-file overload delegates to it (unchanged behavior for single-file graphs).
- [Inliner.cs](src/ScadBundler.Core/Inlining/Inliner.cs) `GatherUseImports`: constant neededness =
  the **union** of `PrivateConstants(closure)` over all use targets — a file shared by two closures is
  imported once but must carry a constant if *either* closure reaches it.
- **Spec fixed too:** [Slice-4-Semantic.md](docs/slices/Slice-4-Semantic.md) §7 post-v1 amendment
  ("own" = the file's include closure); V1–V3 marked verified in
  [Development-Slices.md](docs/Development-Slices.md); corpus layout/TODO updated in
  [Test-Corpus.md](docs/Test-Corpus.md).
- **Tests:** `PrivateConstants_MergedClosure_ReachesConstantsDeclaredInIncludedFiles`
  (`SemanticModelTests`), `Use_DefinitionReadingItsIncludesConstant_CarriesTheConstant`
  (`Slice5BundleTests`, prefix-agnostic assertions). All `B-*` goldens unchanged. **621 tests.**

### Done this session (2026-06-10, session 2) — last-wins winner emit position + `keep-first` rationale docs

For include-origin variable collisions the inliner emitted the winning `AssignmentStatement` at its
**last** document position, so a dependent read between the two (`y = x * 2;`) could precede the
surviving assignment and evaluate `undef` (the SB5008 shape). OpenSCAD evaluates the winning
expression at the **first** assignment's position. **Ground-truth correction:** the in-place overwrite
is in `parser.y` `handle_assignment` → `Assignment::setExpr` (lines 757–790), **not** `LocalScope.cc`
as previously noted here — `LocalScope::addAssignment` is a plain `push_back`; verify there, not in
`LocalScope.cc`.

1. **Fix** ([Inliner.cs](src/ScadBundler.Core/Inlining/Inliner.cs)): for variable groups,
   `KeepLastWins` now marks the **first** occurrence as the winner and records a `_replacements`
   entry (first node `with { Value = last.Value }`); `Assemble` substitutes it at emit time
   (`Substituted`). The first occurrence's slot/trivia survive; references inside the moved
   expression still hit the rename map (the `Value` subtree keeps its node identities).
   Modules/functions are scope-wide in OpenSCAD, so they keep the old keep-last-node path.
2. **Deliberate non-change — do not "fix" later:** `--on-collision keep-first` still keeps the first
   assignment's **original** expression. It is a deliberate-divergence repair strategy (no OpenSCAD
   analogue), not emulation — do not extend the in-place substitution to `KeepFirst` for consistency.
   Rationale now documented: [UX.md](docs/UX.md) **"Collision Strategies"** (new section: per-strategy
   table, why `keep-first` exists — vendored-include stomps, version-skew diamonds — and its caveats),
   [Spec.md](docs/Spec.md) "Collision-strategy implication", the `CollisionStrategy.KeepFirst` XML
   docs, and the SB3003 note in [Diagnostics.md](docs/Diagnostics.md).
3. **Tests:** `IncludeVariableReassignment_EmitsWinningExpressionAtFirstPosition`
   (`Slice5BundleTests`) — winner's value (`5`) at lib's slot ahead of the dependent read, SB3003
   still fires, SB5008 silent. **582 tests.** No corpus goldens affected (none collide on variables).
4. **Differential validation (official binary):** repro (lib `x = 1; y = x * 2;`, root
   `include <lib.scad>; module m() …; x = 5;` — the override *after* the first definition so the
   prologue hoist doesn't mask it) → bundle → `openscad.com -o *.csg` both sides → **byte-identical**
   (`cube([5, 10, 1])`, i.e. `y = 10`; the pre-fix bundle yields `y = undef`).

### Done earlier (2026-06-10, session 1) — Customizer computed-params regression fix + SB5008

The prologue hoist from Item A was too greedy: it hoisted **computed** root assignments (e.g.
`cleat_spacing_x = goews_staggered_x_spacing;`) above the included library that assigns their inputs.
OpenSCAD evaluates top-level assignments in document order, so the hoisted reads became `undef`
(~28 warnings on `ForkedHolder.bundled.scad`).

1. **Literal-only hoist.** `ExtractPrologue` now gates on `IsCustomizerLiteral`
   ([Inliner.cs](src/ScadBundler.Core/Inlining/Inliner.cs)), a mirror of OpenSCAD
   `Expression::isLiteral()` — the exact gate `CommentParser::collectParameters` uses (ground truth:
   `src/core/customizer/CommentParser.cc`, `src/core/Expression.cc`): literals, unary ops over
   literals, all-literal vectors/ranges; parens transparent. A computed assignment was never a
   Customizer parameter — it stays at its document position (after the includes that feed it) and
   does **not** end the prologue run (OpenSCAD keeps collecting literals past it).
2. **SB5008 forward-reference safety net.** New
   [ForwardReferenceChecker.cs](src/ScadBundler.Core/Inlining/ForwardReferenceChecker.cs) runs after
   assembly: warns when any top-level assignment in the **final** bundle reads a variable whose first
   top-level assignment comes later. Eager positions only — function-literal bodies/defaults are
   call-time, call *callees* are scope-wide function refs, `let`/`for`/comprehension bindings shadow,
   `$vars`/`PI` exempt. Cataloged in [Diagnostics.md](docs/Diagnostics.md).
3. **Tests/goldens:** unit tests for literal-form classification, the regression shape, and SB5008
   positive/negative cases; new corpus golden **`B-009-computed-params`** (the ForkedHolder shape:
   author's own `/* [Hidden] */` marker hoisted with its literal, computed param below the inlined
   lib); `B-008` re-blessed (`ratio` now sits in the `main.scad (continued)` section). **581 tests.**
4. **Real-world differential validation (official binary):** `ForkedHolder.scad` and
   `HexContainer.scad` → bundle → `openscad.com -o *.csg` both sides → **zero warnings, byte-identical
   CSG**. Note: `C:\git\dan\SCAD\*.bundled.scad` artifacts on disk may predate the fix — regenerate
   before eyeballing.

### Done earlier — attribution pass

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
  aggregation + provenance banners) + the Customizer computed-params regression fix (SB5008) + the
  last-wins winner-position fix + **the OpenSCAD integration harness** + the PrivateConstants
  include-closure fix:
  `dotnet build` zero-warning (warnings-as-errors), `dotnet test` green (**621 tests**: 569 in
  `ScadBundler.Core.Tests`, 20 in `ScadBundler.Cli.Tests`, 32 in `ScadBundler.IntegrationTests`).
  Coverage: `Lexing/`≈98%, `Parsing/`≈99%, `Semantics/` 100% (incl. the new closure code), `Loading/`≈98.8%,
  `Inlining/`: `Attribution`/`StructuralKey`/`Bundler` 100%, `BundleRewriter`≈99.6%, `Inliner.cs`≈98%,
  **`ForwardReferenceChecker.cs`≈60% (known gap — see watch items)**, `Emitting/`: `Emitter.cs`≈97%,
  `EmitOptions.cs` 100%.
- **Post-demo (this session), see [docs/Post-Demo-Plan.md](docs/Post-Demo-Plan.md):**
  - **A — Customizer parameters preserved.** The root file's leading **literal** parameter assignments are hoisted to the top of the bundle (verbatim, never renamed) and a synthesized `/* [Hidden] */` fences the rest, so OpenSCAD's Customizer shows the model's real knobs instead of an included library's globals. Computed assignments are not parameters (OpenSCAD `Expression::isLiteral` gate) and keep their document position — see "Done earlier (2026-06-10, session 1)". Verified on `C:\git\dan\SCAD\ForkedHolder.scad`. ([Inliner.cs](src/ScadBundler.Core/Inlining/Inliner.cs); goldens `slice5-bundle/B-008`, `B-009`.)
  - **B — OpenSCAD-faithful search paths.** New [OpenScadEnvironment.cs](src/ScadBundler.Core/Loading/OpenScadEnvironment.cs) reconstructs OpenSCAD's `parser_init` order: absolutized `OPENSCADPATH` (empty→CWD) + the per-user library folder. Wired through `Bundler`/`BundleCommand`.
  - **C (`--qualify-all`)** and **D (obfuscator, vNext)** remain scoped but unimplemented.
- Branch is **`Claude_implementation`**. Last feature commits (2026-06-10, session 3): `fix(semantics): collect use'd private constants over the include closure` + `feat(tests): add OpenSCAD differential integration harness (V1–V3)`.
- **Projects:** `src/ScadBundler.Core` (the library), **`src/ScadBundler`** (the CLI, `PackAsTool` → `scadbundler`), `tests/ScadBundler.Core.Tests`, **`tests/ScadBundler.Cli.Tests`**, **`tests/ScadBundler.IntegrationTests`** (env-gated differential harness). All five are in `ScadBundler.sln`.
- **Entry points:** `Bundler.Bundle(rootPath, options)` (disk + `OPENSCADPATH`) → `BundleResult`; `Emitter.Emit(scadFile, EmitOptions?)` → `string`. The CLI wires them in `src/ScadBundler/BundleCommand.cs`.

## What Slice 6 added

- **`Emitting/Emitter.cs`** — a deterministic, idempotent recursive pretty-printer. Numbers/strings via `RawText`; author `ParenthesizedExpression` preserved; **precedence-minimal parens** inserted only around synthesized subtrees (thresholds aligned to `docs/Parser-Planning.md`); leading comments on their own indented lines, trailing comments after two spaces, `BlankLineBefore` → one blank line; `--minify` (drops comments/blank lines/optional whitespace, keeps token-separating spaces via a word-char guard). `Emitter.RoundTripsStructurally` is the internal SB6001 self-check (re-parse + `StructuralKey` compare) used by tests.
- **`Emitting/EmitOptions.cs`** — `IndentWidth`/`IndentStyle`/`BraceStyle`/`MaxLineLength` (advisory)/`Minify`/`PreserveComments`. Defaults lock the goldens.
- **`src/ScadBundler` CLI** — `scadbundler bundle <in> [opts]` with every `docs/UX.md` option (`-o`/`-p`/`--on-collision`/`--bundle-licenses`/`--[no-]preserve-comments`/`--minify`/`--dry-run`/`--diff`/`--verbose`); diagnostics grouped by severity to stderr; exit `0`/`1` (any Error diagnostic)/`2` (bad args).
- **Goldens:** `tests/Corpus/slice5-bundle/*/expected.scad` (B-001..B-007, now exact) and `tests/Corpus/slice6-emit/*` (EM-001 Customizer trivia, precedence, control-flow, comprehensions). Regenerate with `BLESS_EMIT=1`.
- **`SB6001`** added to `DiagnosticCode.cs` (the emitter self-check code; reserved/internal).

## Watch items / known gaps (from the Slice-5 cold review)

- ~~**Last-wins winner emitted at the wrong position**~~ **Resolved (2026-06-10, session 2):** variable
  groups now emit the winning expression at the **first** colliding occurrence's slot (`KeepLastWins`
  records a `_replacements` substitution; `Assemble` applies it), matching OpenSCAD's in-place overwrite
  — ground truth `parser.y` `handle_assignment` → `Assignment::setExpr`, *not* `LocalScope.cc` (plain
  `push_back`). Differential-validated byte-identical CSG. `keep-first` deliberately exempt (see "Done
  this session (2026-06-10, session 2)").
- **Prologue cutoff approximates `getLineToStop`** *(accepted, conservative)*: OpenSCAD collects literal
  params up to the **first `{` line** of the root text; our prologue run ends at the first
  definition/instantiation/control-flow statement. A brace-free instantiation (`cube(1);`) followed by
  more assignments would hoist fewer params than OpenSCAD shows — never more. Revisit only on a report.
- **Group marker attached to a computed assignment** *(accepted, cosmetic)*: if an author writes
  `/* [Group] */` directly above a *computed* assignment, the marker stays in the body with it, and the
  following hoisted literals fall under the previous group in the Customizer. Markers above literal
  assignments (the normal case, incl. author `/* [Hidden] */`) hoist correctly with their parameter.
- **`ForwardReferenceChecker.cs` line coverage ≈60%** *(pre-existing; surfaced by this session's
  coverage pass — the earlier "`Inlining/`≈99.6%" claim glossed it)*: lines 115–208, the read-walk
  exemption branches (function-literal bodies/defaults, `let`/`for`/comprehension shadowing, `$`-vars),
  are untested. Below the ≥95% bar; a small focused session.
- **V-001/V-003 integration fixtures need a binary that still evaluates `child()`/`assign()`** — the
  installed 2021.01 does (as `DEPRECATED:`). A future OpenSCAD that removes them would fail those two
  facts (V-001 would render without the child geometry); point `OPENSCAD_EXE` at a 2021.01 install.

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
- ~~**Integration harness (V1–V3)** against the official OpenSCAD C++ engine~~ — **done (2026-06-10,
  session 3):** the recipe that used to live in this bullet is now executable code in
  `tests/ScadBundler.IntegrationTests` (`DifferentialAssert` + `OpenScadCli`/`OpenScadStderr`); the
  official fixtures and Dan's real projects run differentially on every `dotnet test` when OpenSCAD is
  present. See "Done this session (2026-06-10, session 3)".
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

- Commits on `Claude_implementation`, **conventional commits**, ending with the current model's `Co-Authored-By` trailer (e.g. `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`). Commit when a unit is done; don't push unless asked.
- `.gitattributes` forces **LF**; `.editorconfig` enforces file-scoped namespaces, `var`-only-when-apparent, no top-level statements, and warnings-as-errors. Every **public** Core member needs XML docs (CS1591); watch CA1859/CA1822.
- If you find a genuine spec gap/ambiguity, **fix the spec too** (one-shot, spec-driven). Slice 6 locked the keyword-paren spacing rule in `docs/slices/Slice-6-Emitter-CLI.md` §5.
