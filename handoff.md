# Handoff — Start Here (Slice 4: Semantic Analysis & Symbol Table)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1 (lexer), 2 (AST + recursive-descent parser), and 3 (comprehensions + functional expression forms) are complete and committed — the parser is now complete.** Your job this session is **Slice 4 — the `SemanticAnalyzer`**: symbol-table construction, scope resolution, within-scope duplicate detection, the `PrivateConstants` reachability set, and the semantic validation diagnostics (SB3001–SB3005). The output is the `ISemanticModel` that the Slice 5 inliner consumes.

---

## 🔴 DO THIS FIRST: cold code review of Slice 3

**Before writing any Slice 4 code, do a fresh, critical review of the Slice 3 implementation.** You did not write it this session — read it as a reviewer would. Slice 4 builds on the ASTs the parser produces, so any latent parsing bug surfaces here. Review goals:

- **Parser expression seam.** Read the Slice-3 additions in `src/ScadBundler.Core/Parsing/Parser.cs`: the `ParseExpression` dispatcher (the `function`/`let`/`assert`/`echo` prefix forms) + `ParseTernary`; `CanStartExpression` (the `expr_or_empty` first-set); `ParseVectorElement` and the generators (`ParseForComprehension` incl. C-style, `ParseEachComprehension`, `ParseIfComprehension`, `ParseLetVectorElement`, `ParseParenthesizedGenerator`); `IsComprehensionGenerator` (the trailing-`let` classifier, which unwraps one paren layer); the `ParseArgumentList(allowSemicolonTerminator)` overload (C-style `for`). Cross-check against OpenSCAD `C:\git\hub\openscad\src\core\parser.y` (`expr`, `list_comprehension_elements`, `vector_element`).
- **Golden corpus.** `tests/Corpus/slice3-expr/` (E-009..E-012 + C-001..C-005) and `tests/Corpus/slice3-examples/` (vendored CC0 OpenSCAD `examples/Functions/*.scad`, asserted to parse with **zero** diagnostics). The goldens were blessed via `BLESS_AST=1` and hand-verified against `docs/AST-Reference.md` §14.6 and `docs/Test-Corpus.md`.
- **Tests.** `ParserComprehensionTests`, `ParserFunctionalExprTests`, `ParserRealWorldTests`, `Slice3CorpusTests`.
- **Smells / cleanups.** Fix small issues in a separate tidy commit *before* Slice 4.

Optional: run `/code-review` on the last commit. Record anything non-trivial, then proceed.

---

## Current state

- **Slices 1–3 done:** `dotnet build` zero-warning (warnings-as-errors), `dotnet test` green (**241 tests**), `Lexing/` ≈98% and `Parsing/` ≈98.7% line coverage. The 8 uncovered `Parser.cs` lines are all pre-existing Slice-2 defensive forward-progress guards, not new code.
- Branch is **`Claude_implementation`**. Last feature commit: `feat(parser): implement Slice 3 …`.
- `src/ScadBundler.Core/`: `Text/`, `Trivia/`, `Diagnostics/`, `Lexing/`, **`Ast/`** (hierarchy + visitor), **`Parsing/`** (`Parser`, `TokenCursor`, `ParseResult`). **`Semantics/`** (this slice), `Loading/`, `Inlining/`, `Emitting/` do not exist yet.
- **No new diagnostic codes were added in Slice 3** — comprehension *position* validation is deferred to Slice 4 (**SB3002**, comprehension-outside-vector). The parser only *constructs* generators inside `[ … ]`.

## What to read, in order

1. **`docs/slices/Slice-4-Semantic.md`** — your primary spec (`ISemanticModel` API, the scoping rules §5, `PrivateConstants` reachability §7, the validation diagnostics, `S-001`/`S-002`). **This slice owns `ISemanticModel`.**
2. **`docs/Spec.md`** — `use`/`include` scoping semantics (the distinction that drives collision resolution later).
3. **`docs/AST-Reference.md`** — node shapes and the reference-keyed side-table pattern; **§10** (the polymorphic keyword map) is relevant for classifying references.
4. **`docs/Builtins-Reference.md`** — reserved/built-in names (so built-in calls don't resolve to user symbols).
5. **`docs/Diagnostics.md`** — the SB3xxx catalog. **Catalog any new code there before using it.**
6. Ground truth: OpenSCAD `C:\git\hub\openscad\src\core\` — `ScopeContext.cc`, `LocalScope.cc` (last-wins), `Context.cc`. Verify, don't guess.

## Slice 4 seam (parser → semantics)

- **Input** is a load graph (Slice 5 owns the loader; for Slice 4, drive the analyzer from parsed `ScadFile`s / a minimal graph stub per the spec's API). **Output** is `ISemanticModel` + diagnostics; **never throw** — collect diagnostics.
- **New project area:** `src/ScadBundler.Core/Semantics/` (`SemanticAnalyzer`, `ISemanticModel`, `Symbol`, `SymbolKind`). Mirror the existing project conventions (XML docs on public members, `Composite`/visitor patterns where applicable).
- **Use the visitor.** `IAstVisitor<TResult>` is closed and exhaustive over all 40 records — a scope-building walk is a natural visitor. Reference→declaration binding is keyed by **reference identity** (`ReferenceEqualityComparer`), per the API.
- **SB3002 lives here, not in the parser:** a comprehension generator (`ForComprehension`/`ForCComprehension`/`IfComprehension`/`LetComprehension`/`EachExpression`) appearing anywhere other than inside a `VectorExpression` element is the semantic error. The parser already guarantees generators only *appear* as vector elements, so SB3002 is about generators reached via other AST positions (e.g. a `let`-expression body) — read the spec § on SB3002 carefully.

## Conventions carried over (so the build stays green)

- **Strict build:** `Directory.Build.props` sets net10.0, nullable, `TreatWarningsAsErrors`, analyzers `latest-Recommended`, `GenerateDocumentationFile`. Every **public** Core member needs XML docs (CS1591 = error). Watch **CA1859** (private/internal helpers that always return one concrete type must declare that type — Slice 3's single-node parsers declare concrete returns; the branching ones return `Expression`).
- **AST `Span`** is a defaulted property (`= SourceSpan.Synthetic`), **not** `required`. AST records are immutable; transform via `with`.
- **Golden corpus:** the on-disk `expected.ast` format is finalized (`AstDump`); regenerate with `BLESS_AST=1 dotnet test --filter "FullyQualifiedName~Slice3CorpusTests.Regenerate"` (per-slice Regenerate facts). Always hand-verify blessed goldens against the spec before committing.
- **Diagnostics:** add any new SB3xxx code to `docs/Diagnostics.md` *and* `DiagnosticCode.cs` (with XML doc) before using it. Don't invent codes ad hoc.

## Non-negotiables (Constitution)

Hand-written passes — **no parser generators / ANTLR / regex** in the core path. Immutable AST records. **Collect diagnostics, never throw.** **≥95% line coverage** of `Semantics/`. No runtime interop with OpenSCAD's C++ (reference/fixtures only).

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~SemanticAnalyzerTests"
dotnet test --collect:"XPlat Code Coverage"
```

## Workflow / repo conventions

- Commits on `Claude_implementation`, **conventional commits**, ending with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Commit when a unit is done; don't push unless asked.
- `.gitattributes` forces **LF** everywhere.
- If you find a genuine spec gap/ambiguity, **fix the spec too** — this project's whole point is one-shot, spec-driven implementation. (Slice 3 did this for the parenthesized-generator lookahead in `Slice-3-Parser-Expressions.md` §5.)

## After Slice 4

Slice 5 (`docs/slices/Slice-5-Loader-Inliner.md`) adds the `SourceLoader` (recursive `include`/`use` resolution, search paths, cycle detection) and the `Inliner` (cross-file collision resolution + flattening, consuming this slice's `ISemanticModel`). Pipeline: `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter`.
