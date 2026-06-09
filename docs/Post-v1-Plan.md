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
| 1 | `--on-collision error` hard-fail | Bug (cosmetic) | S | Low | **Done** (this session) |
| 2 | License aggregation (`--bundle-licenses`) | Feature | M | Low | Own session — do next |
| 3 | `include`/`use` leading trivia dropped on flatten | Bug | S–M | Low | Fold into #2 |
| 4 | Cross-`include` mis-bind under non-`Auto` strategies | Correctness bug | M | **High** | Own session, repro-first |
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

---

## 3. `include`/`use` leading trivia dropped on flatten

- **Symptom:** flattening replaces the `IncludeStatement`/`UseStatement` node with its target's
  contents (`Inliner.FlattenIncludes`), discarding that node's `LeadingTrivia`. A license header (or any
  explanatory comment) written immediately above an `include` line is lost.
- **Relationship to #2:** the license case is the one that matters in practice and is solved by #2's
  collection step. General trivia preservation on dropped `include`/`use` lines is a strict superset.
- **Disposition:** **fold into #2.** Handle license headers there; only pursue general
  include-line-comment preservation if a concrete need appears (it risks duplicating now-inlined banners).

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
- **Effort:** Medium. **Risk:** High. **Disposition:** own focused session.

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
2. **License aggregation (#2, absorbing #3)** — additive, low-risk, user-visible.
3. **Cross-`include` mis-bind (#4)** — correctness; its own session, repro-first, `Auto` goldens guarded.
4. **Block-scope duplicate detection (#5)** — leave deferred; revisit only if a real file demands it.

Broader post-v1 scope (WASM/JSON API + "ScadBundler Live", real-world golden masters, OpenSCAD
integration harness V1–V3, emitter line-length wrapping) is tracked in [handoff.md](../handoff.md) and
[Development-Slices.md](Development-Slices.md) and is out of scope for this triage.
