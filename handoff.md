# Handoff — Start Here (Slice 3: Comprehensions & Functional Expressions)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1 (lexer) and 2 (AST + recursive-descent parser) are complete and committed.** Your job this session is **Slice 3 — the functional sublanguage**: list-comprehension generators inside `[ … ]` and the keyword-prefixed expression forms `let(…) e`, `assert(…) e`, `echo(…) e`, and anonymous `function(…) e`.

---

## 🔴 DO THIS FIRST: cold code review of Slice 2

**Before writing any Slice 3 code, do a fresh, critical review of the Slice 2 implementation.** You did not write it this session — read it as a reviewer would. Slice 3 extends the same parser, so any latent issue compounds. Review goals:

- **Parser core.** Read `src/ScadBundler.Core/Parsing/Parser.cs` end-to-end: the precedence-climbing loop (`ParseBinary`/`ParseUnary`/`ParseExponent`/`ParsePostfix`), the `ParseExpression` entry point (this is where you hook the `let/assert/echo/function` prefixes), `ParseVectorOrRange` (this is where comprehension generators belong), the trivia helpers (`Leaf`/`Composite`/`Spanned`), and recovery (`Synchronize`, the `Expect*` family, SB2001–SB2007). Cross-check binding powers against `docs/Parser-Planning.md` and OpenSCAD `C:\git\hub\openscad\src\core\parser.y`.
- **AST + visitor.** `src/ScadBundler.Core/Ast/` — all 40 records, `IAstVisitor<TResult>`, `Accept`. The Slice-3 records (`ForComprehension`, `ForCComprehension`, `IfComprehension`, `LetComprehension`, `EachExpression`, `LetExpression`, `AssertExpression`, `EchoExpression`, `FunctionLiteral`) are **already defined** — you only add their **parsing**.
- **Test harness.** `tests/ScadBundler.Core.Tests/TestSupport/AstDump.cs` already serializes every Slice-3 node (Header + WriteChildren cases exist), so the golden format is ready. `ParseHelper`, `CountingVisitor`, and the `slice2-parser` corpus runner are reusable patterns.
- **Smells / cleanups.** Fix small issues in a separate tidy commit *before* Slice 3.

Optional: run `/code-review` on the last commit. Record anything non-trivial, then proceed.

---

## Current state

- **Slices 1–2 done:** `dotnet build` zero-warning (warnings-as-errors), `dotnet test` green (**196 tests**), `Lexing/` ≈98% and `Parsing/` ≈98.5% line coverage.
- Branch is **`Claude_implementation`**. Last commit: `feat(parser): implement Slice 2 …`.
- `src/ScadBundler.Core/`: `Text/`, `Trivia/`, `Diagnostics/`, `Lexing/`, **`Ast/`** (hierarchy + visitor), **`Parsing/`** (`Parser`, `TokenCursor`, `ParseResult`). `Semantics/`, `Loading/`, `Inlining/`, `Emitting/` do not exist yet.

## What to read, in order

1. **`docs/slices/Slice-3-Parser-Expressions.md`** — your primary spec (exit criteria, the comprehension grammar, the trailing-`let` rule, `expr_or_empty`, test plan).
2. **`docs/Parser-Planning.md`** §"Ternary and the let/assert/echo/function prefixes are handled at the expr entry point, *outside* the binary climbing loop" — this is the exact hook for the prefix expression forms.
3. **`docs/AST-Reference.md`** §7 (comprehensions), §6 (special expression forms), §10 (polymorphic keyword map: `if`/`for`/`let`/`each`/`assert`/`echo`/`function` by context).
4. **`docs/Test-Corpus.md`** cases **E-009..E-012** (trailing-`let`, anonymous function, C-style for, assert-with/without-body) and §4 (the finalized `expected.ast` format — already implemented in `AstDump`).
5. Ground truth: OpenSCAD `C:\git\hub\openscad\src\core\parser.y` (the `list_comprehension_elements`, `expr`, and `vector_expr` rules). Verify, don't guess.

## How to extend the parser (the Slice 2 → Slice 3 seam)

- **Prefix expression forms** — hook at the top of `Parser.ParseExpression()`: if the first token is `Let`/`Assert`/`Echo`/`Function`, parse that form before the ternary/binary climb. Bodies of `assert`/`echo` are **optional** (`expr_or_empty`) → AST `Body` is nullable. `function (params) body` → `FunctionLiteral`. These are value-producing, so they nest as any expression (e.g. a function-def body, an argument, a vector element).
- **Comprehension generators** — only valid as **vector elements**. Add a `ParseVectorElement` used by `ParseVectorOrRange`: if the element starts with `For`/`Let`/`Each`/`If`, parse the generator (`ForComprehension` / `ForCComprehension` / `IfComprehension` / `LetComprehension` / `EachExpression`); otherwise a plain `expr`. Nesting (`for (a) for (b) e`) = a generator whose body is another vector element.
- **C-style `for`** — `for ( bindings ; cond ; bindings ) body` → `ForCComprehension`; distinguish from `for ( bindings ) body` by detecting the `;` after the first binding list.
- **Trailing-`let` rule (E-009, the subtle one)** — inside `[ … ]`, `let(…)` whose body is a comprehension generator → `LetComprehension`; whose body is a plain value → `LetExpression` element. `if` inside `[ … ]` without `else` is a **filter** (`Else=null`); with `else` it selects.
- **Reuse what exists:** `ToBindings`/argument parsing, the `Leaf`/`Composite`/`Spanned` trivia helpers, and `AstDump` (already renders all Slice-3 nodes). Build a `slice3-expr/<id>/{input.scad,expected.ast}` corpus the same way as `slice2-parser` (the `Slice2CorpusTests` + `BLESS_AST=1` regenerate pattern is the template).

## Slice 3 gotchas

- **`each`/`for`/`if`/`let` are dual-purpose.** In **statement** position they are control flow (Slice 2, already done). As **vector elements** they are comprehension generators (this slice). `each` has no statement form. Keep the two paths separate.
- **`function` token in expression position** → `FunctionLiteral`; in statement position it is a `FunctionDefinition` (Slice 2). Same token, disambiguated by position.
- **Comprehension-outside-vector is NOT a parse error** — the parser only accepts generators inside `[ … ]`; a generator appearing elsewhere is caught by the **semantic** pass (SB3002, Slice 4). Don't emit a parser diagnostic for it; just don't parse it outside a vector.
- **Member access `.x/.y/.z` and immediately-invoked literals** (`(function (x) x)(5)`) already work via Slice 2 postfix — verify with a test, no new code expected.

## Conventions carried over (so the build stays green)

- **Strict build:** `Directory.Build.props` sets net10.0, nullable, `TreatWarningsAsErrors`, analyzers `latest-Recommended`, `GenerateDocumentationFile`. Every **public** Core member needs XML docs (CS1591 = error). Watch **CA1859** (private/internal helpers that always return one concrete type must declare it — bit me repeatedly).
- **AST `Span`** is a defaulted property (`= SourceSpan.Synthetic`), **not** `required` — so you can `new Node(...)` then attach span/trivia via the `Leaf`/`Composite`/`Spanned` `with`-helpers. Follow that pattern.
- **Trivia model (Slice-2 §9):** statements + atom-bounded expressions carry leading/trailing trivia; operator/composite expressions carry **span only** (no double-attachment). New comprehension/prefix nodes are composite → use `Composite`/`Spanned`.
- **Golden corpus:** the on-disk `expected.ast` format is finalized (`AstDump`); regenerate with `BLESS_AST=1 dotnet test --filter Regenerate`. Verify generated goldens against the spec before committing.
- **Diagnostics:** if Slice 3 needs a new parser code, catalog it in `docs/Diagnostics.md` first (SB2xxx). Don't invent codes ad hoc.

## Non-negotiables (Constitution)

Hand-written recursive descent + precedence climbing — **no parser generators / ANTLR / regex** in the core path. Immutable AST records. **Collect diagnostics, never throw** (panic-mode recovery). **≥95% line coverage** of `Parsing/`. No runtime interop with OpenSCAD's C++ (reference/fixtures only).

## Definition of Done (Slice-3)

Comprehension generators (`for`, C-style `for`, `if`/`else`, `let`, `each`) parse inside vectors; the `let`/`assert`/`echo`/`function` expression forms parse with correct nesting and optional bodies; **E-009..E-012** and the AST-Reference §14.6 example parse to the documented trees; the trailing-`let` rule is correct; build/tests green zero-warning; `Parsing/` coverage ≥95%.

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~ParserExpressionTests"
BLESS_AST=1 dotnet test --filter "FullyQualifiedName~Slice2CorpusTests.Regenerate"  # bless goldens
dotnet test --collect:"XPlat Code Coverage"
```

## Workflow / repo conventions

- Commits on `Claude_implementation`, **conventional commits**, ending with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Commit when a unit is done; don't push unless asked.
- `.gitattributes` forces **LF** everywhere.
- If you find a genuine spec gap/ambiguity, **fix the spec too** — this project's whole point is one-shot, spec-driven implementation.

## After Slice 3

Slice 4 (`docs/slices/Slice-4-Semantic.md`) adds the `SemanticAnalyzer`: symbol tables, scope resolution, collision detection, and the semantic diagnostics (SB3001 member validation, SB3002 comprehension-outside-vector, SB3003/4 last-wins, SB3005 unknown reference). Pipeline: `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter`.
