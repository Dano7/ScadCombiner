# Handoff — Start Here (post-v1: pipeline complete)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1–6 are complete and committed** — the compiler pipeline `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter` is **closed end-to-end**, and the `scadbundler` CLI runs it and packs as a global tool. There is no "next slice"; remaining work is **post-v1** (see below). This file orients you to the finished state.

---

## ▶ Next session — start here

The two correctness items that led this file are **both done** (see "Done this session" below). The
remaining post-v1 work is now unblocked; pick the next one:

1. **License aggregation (`--bundle-licenses`)** — [Post-v1-Plan.md](docs/Post-v1-Plan.md) #2 (absorbs #3).
   Additive, low-risk, user-visible: the flag is wired through the CLI but is a silent no-op today.
   Collect + dedup each loaded file's leading license trivia and attach it to the first emitted statement,
   gated on `_options.BundleLicenses`. Reserve **SB5007** if you add the Info marker (coordinate with the
   note in [Post-Demo-Plan.md](docs/Post-Demo-Plan.md) §"Diagnostic codes").
2. **Obfuscator (`--obfuscate`)** — [Post-Demo-Plan.md](docs/Post-Demo-Plan.md) Item D (**vNext**). Now a
   thin layer over the always-namespace work: same candidate set + reference rewrite + prologue exemption,
   only the name *generator* changes. Must use **deterministic** ids (a counter), never memory addresses
   (those break goldens/idempotence).
3. Broader post-v1: WASM/JSON API + "ScadBundler Live", real-world golden masters (BOSL2/NopSCADlib/
   dotSCAD), the OpenSCAD integration harness (V1–V3), emitter line-length wrapping. See §"Post-v1 work".

### Done this session

1. **Cross-`include` mis-bind fixed** ([Post-v1-Plan.md](docs/Post-v1-Plan.md) #4). The repro was
   **`prefix`** (not `keep-first`): both colliding `include` defs survive namespaced, so references are
   *rewritten*, and `NamespaceRep` distributed them per-rep via the pre-inline model's per-file binding —
   a call resolved inside `a.scad` to a.scad's own `part` became `a__part` where the flat bundle requires
   `b__part` (LocalScope.cc last-wins). Fix: split `NamespaceRep`→`RenameDeclaration` + reference rewrite,
   add `ResolvePrefix` that redirects every include-origin reference to the last include-origin def. `Auto`/
   `keep-first`/`keep-last` were already correct (they drop losers + keep names → re-bind by name).
2. **Always-namespace `use` imports** ([Post-Demo-Plan.md](docs/Post-Demo-Plan.md) Item C / **[ADR 0001](docs/adr/0001-include-use-scoping-and-namespacing.md)**).
   Every non-`Protected` `use`-origin symbol is now namespaced *by construction* (`<filestem>__name`), not
   only on a detected clash — matching OpenSCAD's per-file `FileContext` isolation. A **non-clashing**
   import is namespaced **silently** (no SB5004 — it would otherwise fire per library symbol); genuine
   clashes still warn. `include`-origin defs (flat last-wins) and `$`-special-vars (dynamic scope) are
   left untouched. Re-blessed `B-002`; added `B-009-use-isolation`.

**Why (read [ADR 0001](docs/adr/0001-include-use-scoping-and-namespacing.md) first):** OpenSCAD `include`
is a flat textual merge (last-wins) and must **not** be namespaced; `use` is per-file `FileContext`
isolation and is namespaced *by construction*. The demo's "prefix every identifier" is rejected — it
would break `include` cross-references and `$`-variable dynamic scope. Ground truth: `lexer.l` (include is
lexer-level), `parser.y`/`ScopeContext.cc` (use isolation), verified at `C:\git\hub\openscad`.

---

## Current state

- **Slices 1–6 done** + **post-demo Items A/B** + **#4 mis-bind & Item C always-namespace `use`** (this session): `dotnet build` zero-warning (warnings-as-errors), `dotnet test` green (**549 tests**: 531 in `ScadBundler.Core.Tests`, 18 in `ScadBundler.Cli.Tests`). Coverage: `Lexing/`≈98%, `Parsing/`≈99%, `Semantics/` 100%, `Loading/`≈98.8%, `Inlining/`≈99.6%, **`Emitting/`: `Emitter.cs`≈97%, `EmitOptions.cs` 100%**.
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

## Watch items / known gaps (from the Slice-5 cold review this session)

- **`BundleOptions.BundleLicenses` and `.PreserveComments` are not read by the `Inliner`.** `--bundle-licenses` is wired through the CLI but currently a **no-op** (license aggregation was never implemented). `--preserve-comments` is honored where it belongs — in the **emitter** (`EmitOptions.PreserveComments`). Implementing license aggregation (collect + dedup leading license trivia on the root) is a clean post-v1 task.
- **`include`/`use` leading trivia is dropped on flatten** (the statement is replaced by its target's contents). A license header riding on the root's `include` line is lost. Tied to the `--bundle-licenses` gap above.
- ~~**Latent cross-`include` mis-bind under non-`Auto` strategies**~~ **Resolved (this session):** the failing strategy was **`prefix`** — `NamespaceRep` rewrote cross-`include`-duplicate *references* per-rep via `ISemanticModel.ReferencesTo`, trusting the pre-inline model's per-file binding. `ResolvePrefix` now redirects every include-origin reference to the last include-origin definition (LocalScope.cc last-wins). `Auto`/`keep-first`/`keep-last` were already correct. See [Post-v1-Plan.md](docs/Post-v1-Plan.md) #4.
- ~~**`CollisionStrategy.Error`** emits the same collision *warnings* as `Auto` and returns an empty bundle (no dedicated error-severity code), so the CLI exits `0` with empty output for that mode.~~ **Resolved (post-v1):** a genuine collision under `--on-collision error` now emits **SB5006** (Error-severity, one per colliding site) and the CLI exits `1` with no output. See [docs/Post-v1-Plan.md](docs/Post-v1-Plan.md).

## Post-v1 work (see `docs/Development-Slices.md`)

- **WASM/JSON API + "ScadBundler Live"** web companion (the Core is dependency-free and consumable for this).
- **Real-world golden masters**: small slices of BOSL2 / NopSCADlib / dotSCAD.
- **Integration harness (V1–V3)** against the official OpenSCAD C++ engine (test-only; render-equivalence). Ground truth checkout at `C:\git\hub\openscad`; fixtures in its `examples/` and `tests/data/modulecache-tests/`.
- **`--bundle-licenses`** aggregation + line-length wrapping in the emitter (both stubbed/advisory today).

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
