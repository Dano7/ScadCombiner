# ScadBundler Development Slices / Iterations

## Overall Development Strategy
Build **incrementally, test-driven**, with AI assistance. Grammar references will guide implementation to avoid cul-de-sacs.

**Approach**:
- Each slice produces a working, testable milestone.
- Heavy use of Grammar-References.md and Parser-Planning.md.
- Test-Driven + Golden Masters early.

## Slice 0: Preparation ✓
- Review grammar resources.
- Finalize AST design.
- Collect test corpus.

## Slice 0.5: Documentation Completeness ✓ (Complete)

**Goal**: Every subsequent slice must be *one-shot AI ready* — a cold AI assistant can implement the slice and self-verify the milestone with no additional clarification.

**Exit Criteria for Slice 0.5**:
- [x] Each slice has a precise, testable acceptance checklist (not vague goals). → every slice spec has an **Exit Criteria** section.
- [x] AST node hierarchy is fully specified: record names, field names, field types, nullable/optional annotations. → [AST-Reference.md](AST-Reference.md)
- [x] Grammar coverage per slice is explicitly listed. → Slice 1–3 specs + [Parser-Planning.md](Parser-Planning.md).
- [x] Error/diagnostic catalog: every user-visible error has a code, message template, and triggering condition. → [Diagnostics.md](Diagnostics.md) (SB1xxx–SB6xxx; minor edge cases tracked under "To Be Cataloged").
- [x] Golden test cases per slice (happy + error paths). → [Test-Corpus.md](Test-Corpus.md) (L/P/E/S/B/EM); each slice's test plan expands them during implementation.
- [x] All docs are internally consistent (cross-checked after every change).
- [x] Slice boundaries are unambiguous. → each slice spec has an explicit **Scope (In/Out)**.
- [x] `include` vs `use` semantics are precisely specified with examples in Spec.md.
- [x] Collision resolution strategies are fully specified with examples. → [Spec.md](Spec.md) "Collision-strategy implication" + Slice 5 §6 + B-006/B-007.

**Status**: ✅ **Slice 0.5 essentially complete** — all six implementation slices are spec-ready and mutually consistent. Ready to begin implementation (Slice 1) or to run the AI-assistant comparison experiment.

**Deliverables**:
- Updated: `Spec.md`, `Design.md`, `Parser-Planning.md`, `Constitution.md`, `UX.md`, `Development-Slices.md`, `README.md`, `CLAUDE.md`.
- New reference docs: `AST-Reference.md`, `Diagnostics.md` (SB1xxx–SB6xxx), `Builtins-Reference.md`, `Test-Corpus.md`.
- New per-slice specs: all six under `slices/` — `Slice-1-Lexer` … `Slice-6-Emitter-CLI`.

## Slice 1: Project Setup & Lexer ✓ **spec ready**

Full spec: **[slices/Slice-1-Lexer.md](slices/Slice-1-Lexer.md)**.

**Scope**: .NET 10 solution + Core/Tests projects + build/analyzer config; foundational text & trivia types; diagnostics plumbing; hand-written lexer (all token kinds, hex/escape decoding, comment trivia, `BlankLineBefore`, precise source spans) with diagnostics SB1001–SB1009. **Exit**: zero-warning build, green xUnit, corpus L-001..L-004 + token battery pass, ≥95% lexer coverage.

## Slice 2: Parser — Statements & Core Expressions ✓ **spec ready**

Full spec: **[slices/Slice-2-Parser.md](slices/Slice-2-Parser.md)**.

**Scope**: complete AST hierarchy (40 records + visitor); recursive-descent statement parser (defs, assignments, include/use, module instantiation + modifiers + children, if/else, name-recognized for/intersection_for/let); precedence-climbing parser for all ordinary expressions (binary cascade, unary, ternary, exponent, postfix, primary, vectors, ranges); parameters/arguments; trivia propagation; panic-mode recovery (SB2001–SB2007). **Exit**: E-001..E-008 + P-001..P-003 + AST §14 examples parse correctly; ≥95% parser coverage.

## Slice 3: Parser — Comprehensions & Functional Expressions ✓ **spec ready**

Full spec: **[slices/Slice-3-Parser-Expressions.md](slices/Slice-3-Parser-Expressions.md)** — extends the Slice 2 parser (AST records already defined).

**Scope**: vector-context comprehension generators (`for`, C-style `for(;;)` → `ForCComprehension`, `if`/`else`, `let`, `each`) incl. the trailing-`let` disambiguation; `expr`-level forms `function(…) e`, `let(…) e`, `assert(…) e?`, `echo(…) e?` (nullable bodies). **Exit**: comprehension + functional-expr suites (E-009..E-012) pass; OpenSCAD `examples/Functions` parse clean; parser complete.

## Slice 4: Semantic Analysis & Symbol Table ✓ **spec ready**

Full spec: **[slices/Slice-4-Semantic.md](slices/Slice-4-Semantic.md)** — owns the `ISemanticModel` contract the Slice 5 inliner consumes.

**Scope**: scope-tree construction; OpenSCAD-accurate name binding/reference resolution (last-wins variables, dynamically-scoped special vars, own→builtins→usedlibs lookup); built-in recognition via [Builtins-Reference.md](Builtins-Reference.md) (unknown names = user/library symbols, no hard-error); `PrivateConstants` transitive reachability (the V2 enabler); within-scope duplicate detection (**SB3003**/**SB3004**); validation (**SB3001**/**SB3002**/**SB3005**). Cross-file collision *resolution* is the Slice 5 inliner, which uses this model's per-file queries + `ReferencesTo`. **Exit**: S-001/S-002 + resolution/PrivateConstants suites pass; ≥95% coverage.

## Slice 5: Source Loader & Inliner ✓ **spec ready**

Full spec: **[slices/Slice-5-Loader-Inliner.md](slices/Slice-5-Loader-Inliner.md)**. Consumes the Slice 4 `ISemanticModel` contract (defined in that doc, §4).

**Scope**: SourceLoader (resolve per Spec search order, parse-cache, include/use graph, cycle detection SB4001/SB4002, font passthrough) + Inliner (inline includes in document order; import `use`d defs + private constants with namespacing-on-collision and reference rewriting; origin-dependent collisions SB5004; structural dedup SB5005; normalize assign→let / child→children / preserve deprecated built-ins SB5001–3; assemble one `ScadFile` with license aggregation + Customizer trivia). **Exit**: B-001..B-007 pass; ≥95% coverage.

## Integration Verification Backlog

Behaviors decided in design that must be confirmed against the official OpenSCAD C++ engine (test-only harness, never shipped). Source: [AST-Reference.md](AST-Reference.md) §16.

- **V1** — `child()` ≡ `children(0)` (first child), `child(n)` ≡ `children(n)`. Gates SB5002.
- **V2** *(resolved from source — `ScopeContext.cc`; now a regression guard)* — A `use`d definition sees its own file's constants and the using file cannot override them. Confirms the `use` private-constant + namespace rule.
- **V3** — `assign(...)` ≡ `let(...)` for binding-preserving rewrite. Gates SB5001.

**Status: all three verified (2026-06-10)** by differential render equivalence — byte-identical CSG plus
no new warning-class stderr — against the official binary (OpenSCAD 2021.01) via
`tests/ScadBundler.IntegrationTests` (`VerificationBacklogTests`; fixtures `tests/Corpus/integration/V-00*`).
V1/V3 rely on 2021.01 still *evaluating* the deprecated constructs (it does, as `DEPRECATED:` warnings the
bundle is allowed to shed). The harness's first full run also caught a real gap — a `use`d file's
private constants must be collected over its **include closure** (its FileContext), not its textual file —
now fixed (see [slices/Slice-4-Semantic.md](slices/Slice-4-Semantic.md) §7 amendment).

## Slice 6: Emitter & CLI ✓ **spec ready**

Full spec: **[slices/Slice-6-Emitter-CLI.md](slices/Slice-6-Emitter-CLI.md)** — the capstone (completes the pipeline end-to-end).

**Scope**: deterministic pretty-printer (configurable indent/brace style; default style locks the `B-*` goldens; precedence-aware parens; trivia/Customizer/license preservation; `--minify`) + the `scadbundler bundle` CLI ([UX.md](UX.md) options, pipeline wiring, diagnostics, exit codes, `dotnet tool` packaging). **Exit**: EM-001/EM-002 + exact B-* goldens pass; CLI end-to-end + exit codes; ≥95% emitter coverage.
