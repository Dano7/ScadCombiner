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

## Slice 0.5: Documentation Completeness (Current)

**Goal**: Every subsequent slice must be *one-shot AI ready* — a cold AI assistant can implement the slice and self-verify the milestone with no additional clarification.

**Exit Criteria for Slice 0.5**:
- [ ] Each slice has a precise, testable acceptance checklist (not vague goals).
- [x] AST node hierarchy is fully specified: record names, field names, field types, nullable/optional annotations. → [AST-Reference.md](AST-Reference.md)
- [ ] Grammar coverage per slice is explicitly listed — which production rules are implemented in which slice.
- [ ] Error/diagnostic catalog: every user-visible error has a code (e.g. `SB-001`), message template, and triggering condition.
- [ ] Golden test cases: each slice has ≥3 input→expected-output (or input→expected-diagnostic) examples, covering the happy path and at least one error path.
- [ ] All docs are internally consistent (no contradictions between Constitution, Spec, Design, UX, and slice plans).
- [ ] Slice boundaries are unambiguous — no feature straddles two slices without a clear cut point.
- [x] `include` vs `use` semantics are precisely specified with examples in Spec.md.
- [ ] Collision resolution strategies are fully specified with examples.

**Deliverables**:
- Updated/expanded versions of: `Spec.md`, `Design.md`, `Parser-Planning.md`, `Development-Slices.md`
- New doc: `AST-Reference.md` — complete node hierarchy with field-level detail ✓ **(done)**
- New doc: `Diagnostics.md` — error/warning catalog with codes, messages, examples ◐ **(seeded; expand per-slice)**
- New doc: `Test-Corpus.md` — golden test cases organized by slice ◐ **(seeded: conventions + one binding case per locked decision; expand per-slice)**

## Slice 1: Project Setup & Lexer

*(To be fleshed out in Slice 0.5)*

**Rough scope**: .NET solution structure, project layout, EditorConfig/analyzer config, Lexer implementation with all token types, source location tracking, lexer unit tests.

## Slice 2: Parser (Core Statements)

*(To be fleshed out in Slice 0.5)*

**Rough scope**: Recursive descent parser for top-level statements — module declarations, function declarations, variable assignments, `include`/`use` statements. Immutable AST record types.

## Slice 3: Parser (Expressions & Full Grammar)

*(To be fleshed out in Slice 0.5)*

**Rough scope**: Pratt/precedence-climbing expression parser, remaining grammar rules, full round-trip parse-then-emit test.

## Slice 4: Semantic Analysis & Symbol Table

*(To be fleshed out in Slice 0.5)*

**Rough scope**: Symbol table construction, scope resolution, collision detection across merged files. Built-in recognition via [Builtins-Reference.md](Builtins-Reference.md) (treat unknown names as user/library symbols — do not hard-error). Validation diagnostics: vector member access ∈ {x,y,z} (**SB3001**), comprehension generators only inside vectors (**SB3002**), variable reassignment last-wins (**SB3003**), definition redefinition (**SB3004**). See [Diagnostics.md](Diagnostics.md).

## Slice 5: Source Loader & Inliner

*(To be fleshed out in Slice 0.5)*

**Rough scope**: Recursive `include`/`use` resolution using the search-path order in [Spec.md](Spec.md) "File Resolution" (file dir → `OPENSCADPATH` → user libs → built-in libs), cycle detection (**SB4001**/**SB4002**), dependency ordering, deduplication. Origin-dependent collision strategy: `include` = last-wins, `use` = namespace/prefix (Spec "Collision-strategy implication"). Implements the `use` private-constant rule, font `use` pass-through, and deprecated-construct normalization: `assign`→`let` (**SB5001**), `child`→`children` (**SB5002**), preserve deprecated built-ins (**SB5003**).

## Integration Verification Backlog

Behaviors decided in design that must be confirmed against the official OpenSCAD C++ engine (test-only harness, never shipped). Source: [AST-Reference.md](AST-Reference.md) §16.

- **V1** — `child()` ≡ `children(0)` (first child), `child(n)` ≡ `children(n)`. Gates SB5002.
- **V2** *(resolved from source — `ScopeContext.cc`; now a regression guard)* — A `use`d definition sees its own file's constants and the using file cannot override them. Confirms the `use` private-constant + namespace rule.
- **V3** — `assign(...)` ≡ `let(...)` for binding-preserving rewrite. Gates SB5001.

## Slice 6: Emitter & CLI

*(To be fleshed out in Slice 0.5)*

**Rough scope**: Pretty-printer with Customizer comment preservation, license aggregation, CLI entry point, NuGet packaging.
