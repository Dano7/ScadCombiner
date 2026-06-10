# Post-v1 Plan — Remaining Gaps & Deferrals

The pipeline (`SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter`) is closed
end-to-end and green. This doc triages the **known gaps** flagged in [handoff.md](../handoff.md) plus the
one **in-code deferral**, with a recommended disposition and a concrete plan for each. It does not
re-litigate the broader post-v1 roadmap (WASM/JSON API, real-world golden masters, the OpenSCAD
integration harness, emitter line-length wrapping) — those live in [handoff.md](../handoff.md) §"Post-v1 work"
and [Development-Slices.md](Development-Slices.md).

Each gap is sized so it can be its own focused session. They are intentionally **not** batched: one is
a feature, one is a subtle correctness bug, one is a design call. Mixing them in a single change would
blur review and strain the quality bar (warnings-as-errors, goldens, "fix the spec too").

## Summary

| # | Item | Type | Effort | Risk | Disposition |
|---|------|------|--------|------|-------------|
| 1 | `--on-collision error` hard-fail | Bug (cosmetic) | S | Low | **Done** |
| 2 | License aggregation (`--bundle-licenses`) | Feature | M | Low | **Done** (+ provenance banners; default **on**) |
| 3 | `include`/`use` leading trivia dropped on flatten | Bug | S–M | Low | **Done** (header runs; folded into #2) |
| 4 | Cross-`include` mis-bind under non-`Auto` strategies | Correctness bug | M | **High** | **Done** |
| 5 | Block-scope duplicate detection (SB3003/SB3004) | Design deferral | M | Med (false positives) | **Keep deferred** — revisit on demand |

---

## 1. `--on-collision error` hard-fail — DONE (this session)

- **Was:** under `--on-collision error`, a genuine collision emitted the same SB3003/SB3004 *warnings*
  as the other strategies and returned an empty bundle, but produced **no Error-severity diagnostic**,
  so the CLI exited `0` with empty output.
- **Now:** a real collision emits **SB5006** (Error-severity, one diagnostic per colliding site,
  naming the other definition's `file:line`); the bundle is emptied and the CLI exits `1`.
- **Where:** [Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs) — `ResolveGroup` `Error` case →
  new `ReportCollisionError`; new code `DiagnosticCode.CollisionError` (SB5006); cataloged in
  [Diagnostics.md](Diagnostics.md). Structural duplicates (SB5005) are not collisions and do not trigger it.
- **Tests:** `ErrorStrategy_Collision_ProducesNoOutput_WithErrorDiagnostic` and
  `ErrorStrategy_NoCollision_BundlesNormally` (Slice5BundleTests); `OnCollisionError_Collision_ExitsOne_NoOutput`
  (CliTests).

---

## 2. License aggregation (`--bundle-licenses`)

- **Symptom:** `--bundle-licenses` is parsed and threaded through the CLI, but
  `BundleOptions.BundleLicenses` ([BundleOptions.cs](../src/ScadBundler.Core/Inlining/BundleOptions.cs))
  is **never read by the `Inliner`** — the flag is a silent no-op.
- **Context:** license headers are plain comments held as `AstNode.LeadingTrivia`
  ([AstNode.cs](../src/ScadBundler.Core/Ast/AstNode.cs)). When a file is `include`d/`use`d, the inliner
  replaces the statement with the target's contents, so a header riding on that line is lost (this is
  gap #3). After flattening, each source file's top-of-file license therefore needs deliberate
  collection — it will not survive on its own.
- **Plan (additive, behind the default-off flag):**
  1. During flattening, collect each loaded file's leading comment trivia that qualifies as a license
     header (heuristic: a leading block/line-comment run on the file's first node, or matching common
     SPDX/`Copyright`/license markers — define the predicate in spec before coding).
  2. Deduplicate by normalized text (same header from a diamond include appears once).
  3. Attach the aggregated, deduped headers as `LeadingTrivia` on the **first emitted statement** of the
     bundled `ScadFile` (in `Inliner.Assemble`), gated on `_options.BundleLicenses`.
  4. The emitter already round-trips `LeadingTrivia`, so no emitter change is expected — verify with a golden.
  5. Optional Info diagnostic `SB5007` ("N license headers aggregated") — catalog it first if added.
- **Tests/goldens:** a `slice5-bundle` corpus case with two differently-licensed libs + a shared diamond
  include; assert one copy of each distinct header at the top, none mid-file. Update the CLI's
  `BundleLicenses_*` test (today it only asserts the flag is accepted).
- **OpenSCAD reference:** none — license handling is a bundling nicety with no OpenSCAD semantics.
- **Effort:** Medium. **Risk:** Low (off by default; purely additive trivia).
- **Disposition:** do this next; it is the most user-visible, self-contained win.

### Resolved (attribution pass — license aggregation + provenance banners, default **on**)

- **Scope grew deliberately** (user decision): beyond hoisting licenses, the bundle is now *attributed* —
  one-line provenance banners (`// ======== include <lib.scad> ========`, `use <…>`, `(continued)` on
  re-entry) separate the inlined sections so a curious reader can map the bundle back to the original
  project. Both ride one switch, now **default on** (`--no-bundle-licenses` opts out): the audience of a
  bundled file is the *downloader* on Thingiverse/MakerWorld, who never sees CLI flags, and silently
  stripping library authors' license headers was the wrong default.
- **Mechanics:** new [Attribution.cs](../src/ScadBundler.Core/Inlining/Attribution.cs) walks the
  `LoadGraph` in encounter order (each file's include/use edges by source offset, depth-first, root
  first), collecting each file's **header run** — the leading comments of its first statement (or the
  EOF trivia of a comments-only file), cut at the first Customizer group marker `/* [Name] */` so the
  Customizer UI is untouched. Runs are deduplicated by normalized text and **moved** (stripped from
  their original statements): the root's header leads unframed, non-root headers follow in a delimited
  block labeled with the `include <…>`/`use <…>` statement that pulled each file in. Banners are
  applied at assembly by watching `Span.File` change between consecutive emitted statements (spans
  survive every rewrite, so this is structural, not inferred). **SB5007** (Info) fires once when ≥1
  non-root header is aggregated; cataloged in [Diagnostics.md](Diagnostics.md).
- **Tests/goldens:** `AttributionTests` (hoist order, dedup-everywhere-strip, group-marker cutoff,
  banner change-tracking + `(continued)`, use-labeling, comments-only file via EOF trivia, off-switch,
  error-strategy suppression); corpus **`B-010-license-aggregation`** (two licensed libs via
  include+use, diamond include, dedup, full annotated golden); CLI default-on/`--no-bundle-licenses`
  tests. All `B-*` goldens re-blessed with banners; `Attribution` 100% line coverage.

---

## 3. `include`/`use` leading trivia dropped on flatten

- **Symptom:** flattening replaces the `IncludeStatement`/`UseStatement` node with its target's
  contents (`Inliner.FlattenIncludes`), discarding that node's `LeadingTrivia`. A license header (or any
  explanatory comment) written immediately above an `include` line is lost.
- **Relationship to #2:** the license case is the one that matters in practice and is solved by #2's
  collection step. General trivia preservation on dropped `include`/`use` lines is a strict superset.
- **Disposition:** **fold into #2.** Handle license headers there; only pursue general
  include-line-comment preservation if a concrete need appears (it risks duplicating now-inlined banners).
- **Resolved with #2:** a header riding the root's first `include`/`use` line is the root's *header run*
  and is hoisted to the top by the attribution pass. Comments above a **non-first** include/use line are
  still dropped (the documented strict-superset case; revisit only on a concrete need).

---

## 4. Cross-`include` mis-bind under non-`Auto` collision strategies

- **Symptom (inherited from Slice 4/5, unchanged):** `--on-collision prefix|keep-first|keep-last`
  rewrites references to cross-`include` duplicate names via `ISemanticModel.ReferencesTo`
  ([Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs) `NamespaceRep`/`KeepFirst`/`KeepLastWins`).
  Because the pre-inline semantic model binds each reference to the **last-wins** declaration, a
  strategy that keeps a *different* duplicate (e.g. `keep-first`) can leave a call bound to the wrong
  node — a latent mis-bind. The **default `Auto` pipeline and every `B-*` golden are correct**; this only
  affects the forced non-`Auto` strategies on genuinely-colliding `include` names.
- **Why it's subtle / risky:** the fix touches reference resolution, where `Auto` correctness must be
  preserved exactly. It is the highest-risk item and must **not** share a session with feature work.
- **Plan (repro-first):**
  1. Write a failing test: two `include`d files defining `module part()` differently, a call to
     `part()`, under `keep-first` — assert the surviving call binds to the kept (first) definition.
  2. Trace how `ReferencesTo` maps onto the kept vs. dropped node; the fix likely means the inliner must
     re-point references at the **kept** node regardless of which duplicate the model resolved them to
     (rather than trusting the model's last-wins binding under a non-last-wins strategy).
  3. Re-verify all `B-*` goldens and the `Auto` path are unchanged.
- **OpenSCAD reference:** `LocalScope.cc` (flat-scope last-wins) defines the `Auto`/`keep-last` truth;
  `keep-first`/`prefix` are bundler-only policies with no OpenSCAD analogue, so correctness here means
  "the emitted call resolves to the definition the strategy kept."
- **Effort:** Medium. **Risk:** High. **Disposition:** **Pulled forward to the next session** (was
  "own focused session"). It is now a **prerequisite** for the decided "always-namespace `use`" work
  ([ADR 0001](adr/0001-include-use-scoping-and-namespacing.md); [Post-Demo-Plan.md](Post-Demo-Plan.md)
  Item C), which reuses the same `ISemanticModel.ReferencesTo` rewrite path. Do this **first**,
  repro-first, with the `Auto` goldens guarded.

### Resolved (this session)

- **Repro:** the failing case was **`prefix`**, not `keep-first`. Under `prefix` both colliding
  `include` definitions survive with namespaced names, so references must be *rewritten* — and
  `NamespaceRep` distributed them per-rep via `ReferencesTo`, which trusts the pre-inline model's
  per-file binding. A reference resolved inside `a.scad` to `a.scad`'s own `part` was rewritten to
  `a__part`, where the flat bundle (LocalScope.cc last-wins) requires `b__part`. `keep-first`/`keep-last`/
  `Auto` were already correct: they drop the losers and keep the survivor's original name, so references
  re-bind by name to the survivor — no rewrite, no mis-bind.
- **Fix:** [Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs) — factored `NamespaceRep` into
  `RenameDeclaration` (decl-only rename + SB5004) and a separate reference rewrite; new `ResolvePrefix`
  namespaces each `use`-origin def per-rep (isolated FileContext) but **redirects every include-origin
  reference to the last include-origin definition** via `RedirectReferences`. The earlier copies survive
  as dead code, exactly as a shadowed definition does in OpenSCAD.
- **Tests:** `PrefixStrategy_CrossIncludeInternalReference_BindsToLastWins` (the fix) and
  `KeepFirstStrategy_CrossIncludeInternalReference_BindsToKept` (guards the keep-first half), both in
  `Slice5BundleTests`. All `B-*` goldens and the `Auto` path are unchanged.

---

## 5. Block-scope duplicate detection (SB3003/SB3004 within a block) — in-code deferral

- **Where:** [SemanticAnalyzer.cs](../src/ScadBundler.Core/Semantics/SemanticAnalyzer.cs) `BuildFileScope`
  (the "deferred by design" comment): SB3003/SB3004 duplicate detection runs at **file scope only**.
  Repeated names inside a module/block body are not flagged.
- **Rationale for deferral:** OpenSCAD's exact block-scope boundary for hoisted assignments is
  ambiguous, and the inliner never renames or merges locals, so flagging block-local duplicates risks
  **false positives** for zero correctness benefit.
- **Note:** the *resolution* side of block-local assignments was recently made correct — references to a
  block-local assignment inside the same `for`/`let`/module body now resolve as locals (via
  `CollectBodyLocals` in `ResolveBoundBody`), instead of raising spurious SB3005. This item is the
  symmetric *detection* side, which remains deferred.
- **Disposition:** **keep deferred (effectively won't-fix)** unless a real-world file demonstrates a
  genuine, unambiguous block-local duplicate worth warning on. If revisited, do it detection-only and
  conservatively (warn only where the block boundary is unambiguous), and gate behind goldens proving no
  false positives on the BOSL2/NopSCADlib/dotSCAD corpus.

---

## Recommended sequence

1. ~~`--on-collision error` hard-fail~~ — **done**.
2. ~~**Cross-`include` mis-bind (#4)**~~ — **done** (prerequisite for always-namespace `use`).
3. ~~**License aggregation (#2, absorbing #3)**~~ — **done** (attribution pass: license aggregation +
   provenance banners, default on; SB5007).
4. **Block-scope duplicate detection (#5)** — leave deferred; revisit only if a real file demands it.

Broader post-v1 scope (WASM/JSON API + "ScadBundler Live", real-world golden masters, OpenSCAD
integration harness V1–V3, emitter line-length wrapping) is tracked in [handoff.md](../handoff.md) and
[Development-Slices.md](Development-Slices.md) and is out of scope for this triage.
