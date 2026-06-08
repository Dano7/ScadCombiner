# ScadBundler Design Document

High-level architecture and the rationale behind the key decisions. Detailed, implementation-ready specs live in the per-slice docs and the reference docs (see the **Document Map** at the end); this document is the orientation layer over them.

## High-Level Architecture

ScadBundler is a true compiler pipeline — **no regex/text hacks in the core path**:

```
SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter
```

1. **SourceLoader** — Resolve `include`/`use` per the OpenSCAD search order, parse each file (cached), build the include/use graph, detect cycles. → [Slice 5](slices/Slice-5-Loader-Inliner.md)
2. **Lexer** — Hand-written token scanner; precise source spans; comment trivia; hex/escape decoding; zero-allocation paths where practical. → [Slice 1](slices/Slice-1-Lexer.md)
3. **Parser** — Recursive descent with precedence climbing for expressions; produces an immutable AST. → [Slice 2](slices/Slice-2-Parser.md) (statements + core expressions), [Slice 3](slices/Slice-3-Parser-Expressions.md) (comprehensions + functional forms)
4. **SemanticAnalyzer** — Scope tree, OpenSCAD-accurate name binding/reference resolution, built-in recognition, transitive private-constant sets; produces the `ISemanticModel`. → [Slice 4](slices/Slice-4-Semantic.md)
5. **Inliner / Transformer** — Flatten `include`/`use` into one AST, resolve collisions, deduplicate, normalize deprecated constructs. → [Slice 5](slices/Slice-5-Loader-Inliner.md)
6. **Emitter** — Deterministic pretty-printer with configurable style; preserves comments/Customizer/licenses; reinserts parentheses as needed. → [Slice 6](slices/Slice-6-Emitter-CLI.md)

The **AST** is the spine: a closed hierarchy of immutable `record`s (full definition in [AST-Reference.md](AST-Reference.md)). Stages 4–6 are visitors over it.

## Key Design Decisions (with rationale)

- **Ground truth is the OpenSCAD C++ source**, not recollection. Grammar/precedence from `parser.y`, lexing from `lexer.l`, scoping from `ScopeContext.cc`, resolution from `parsersettings.cc`, last-wins from `LocalScope.cc`, built-ins from `Builtins::init`. Local checkout: `C:\git\hub\openscad` (`openscad-2019.05-3933`). This caught real correctness points (below).
- **Parse-only, immutable AST.** Nodes carry only syntax (+ source span + comment trivia + a `BlankLineBefore` flag). Resolution/dedup/rename results live in **reference-keyed side tables** (`ReferenceEqualityComparer`), never as fields — keeping the tree pure, cacheable, and web-reusable. ([AST-Reference.md](AST-Reference.md) §15.6)
- **`include` vs `use` are semantically distinct** and this is the correctness core. `include` = full textual-equivalent inline (defs + vars + geometry, last-wins on dup). `use` = import only modules/functions **+ their private constants**, isolated from the using file. ([Spec.md](Spec.md))
- **`use` isolation is a *correctness* requirement, not cosmetics.** A `use`d callable evaluates in its own file's context (verified in `ScopeContext.cc`), so flattening its top-level constants into the global scope would be a bug. The inliner namespaces them on collision and rewrites references. → origin-dependent collision default: `include`=last-wins, `use`=prefix.
- **Deprecated constructs handled head-on** ("No Half Measures"): pure syntax/scope deprecations are normalized (`assign`→`let`, `child`→`children`) with warnings; geometry/IO-affecting built-ins are preserved with an info note. (`assign` is *not* a grammar keyword in modern OpenSCAD — it's a recognized module call, so there is no `AssignStatement` node.)
- **Deduplication** by structural (signature + body) hashing that ignores spans/trivia — merges definitions arriving via diamond include/use; geometry is *not* deduped (semantic equivalence).
- **Numbers are `double`** (OpenSCAD has no integer type); `RawText` preserves emit fidelity (`0xFF`, `1.0`, …).
- **Diagnostics are collected, not thrown** — every stage recovers and reports; codes `SB1xxx`–`SB6xxx` by phase. ([Diagnostics.md](Diagnostics.md))
- **Deterministic emitter** — a fixed default style so golden-master tests are exact; idempotent (`emit∘parse` is a fixed point).
- **Hand-written everything** — no ANTLR/parser generators; recursive descent + precedence climbing for control and debuggability.

## Data Model

- One closed `AstNode` hierarchy (40 concrete types): `Statement` (12) / `Expression` (23) / supporting `Parameter`,`Argument`,`Binding`, plus `CommentTrivia`. Visitor pattern (`IAstVisitor<T>`), optionally source-generated.
- Source provenance on every node (`SourceSpan` incl. the originating `SourceFile`) survives inlining — essential once nodes from many files coexist.
- Customizer metadata is *not* special nodes; it is recovered from comment trivia on top-level assignments. ([AST-Reference.md](AST-Reference.md) §11)

## Extensibility

- Visitor-based transforms for future features (minification exists via `--minify`; dead-code elimination, etc.).
- Plugin model (post-v1).
- **Web support**: the Core library is dependency-free and parse-only, so it can power a WASM/JSON API for the independent "ScadBundler Live" web companion.

## Challenges & Mitigations

- **`use` namespace isolation** → namespace-on-collision + reference rewriting (the V2 guarantee).
- **Module/variable collisions across files** → origin-dependent strategy, configurable via `--on-collision`.
- **Order & last-wins semantics** → preserve document order for `include`; rely on OpenSCAD's last-wins re-applying to merged output.
- **Cycles** → search-stack detection (SB4002); diamonds (DAGs) allowed + deduped.
- **Large libraries (BOSL2)** → parse-cache, low-allocation lexer, streaming emit.
- **Version drift of built-ins** → unknown names treated as user/library symbols, never hard-errors.

## Document Map

**Cross-cutting references** (authoritative):
- [Constitution.md](Constitution.md) — non-negotiable principles.
- [Spec.md](Spec.md) — requirements; `include`/`use`, file-resolution, collision, deprecation semantics.
- [AST-Reference.md](AST-Reference.md) — the complete node hierarchy + visitor + file layout.
- [Parser-Planning.md](Parser-Planning.md) — parsing strategy + the operator precedence/binding-power table.
- [Builtins-Reference.md](Builtins-Reference.md) — built-in modules/functions/special-vars/constants.
- [Diagnostics.md](Diagnostics.md) — the `SBnnnn` catalog (SB1xxx–SB6xxx).
- [Test-Corpus.md](Test-Corpus.md) — golden/decision-proving cases.
- [Grammar-References.md](Grammar-References.md) — external grammars + the local C++ source.
- [UX.md](UX.md) — the CLI surface.

**Implementation slices** ([Development-Slices.md](Development-Slices.md) is the index): [1 Lexer](slices/Slice-1-Lexer.md) · [2 Parser core](slices/Slice-2-Parser.md) · [3 Parser comprehensions](slices/Slice-3-Parser-Expressions.md) · [4 Semantic](slices/Slice-4-Semantic.md) · [5 Loader & Inliner](slices/Slice-5-Loader-Inliner.md) · [6 Emitter & CLI](slices/Slice-6-Emitter-CLI.md).
