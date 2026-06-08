# Handoff — Start Here (Slice 2: AST + Parser)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slice 1 (project setup + lexer) is complete and committed.** Your job this session is **Slice 2 — the AST hierarchy + recursive-descent statement/expression parser.**

---

## 🔴 DO THIS FIRST: cold code review of Slice 1

**Before writing any Slice 2 code, do a fresh, critical review of the Slice 1 implementation.** You did not write it this session — read it as a reviewer would. Slice 2 builds directly on the lexer's token stream, so any latent lexer bug becomes a parser bug. Review goals:

- **Correctness vs ground truth.** Re-check `src/ScadBundler.Core/Lexing/Lexer.cs` against OpenSCAD `C:\git\hub\openscad\src\core\lexer.l` — numbers (hex/fraction/scientific + maximal munch), string escapes, contextual `include`/`use`, the trivia/`BlankLineBefore` model, and SB1001–SB1009 recovery.
- **Token contract you're about to consume.** Make sure you actually understand `LexResult`, `Token`, and the trivia attachment rules (below) before designing the parser around them.
- **Smells / cleanups.** Dead code, off-by-one in spans, naming, anything that will be awkward for the parser. Fix small issues in a separate tidy commit *before* starting Slice 2 so the parser work stays clean.
- **Tests.** Skim the 6 test files under `tests/ScadBundler.Core.Tests/Lexing/` + `TestSupport/`. Confirm the corpus harness (`CorpusLocator`, `LexDump`) is something you can reuse/extend for the AST golden format.

Optional: run `/code-review` on the Slice 1 commit, or just read it. Record anything non-trivial you find. **Then** proceed to Slice 2.

---

## Current state

- **Slice 1 done:** `dotnet build` is zero-warning (warnings-as-errors), `dotnet test` is green (90 tests), `Lexing/` line coverage ≈98%.
- Branch is **`Claude_implementation`** (not `main`). Last commit: `feat(lexer): implement Slice 1 …`.
- What exists in `src/ScadBundler.Core/`: `Text/` (SourceFile, SourcePosition, SourceSpan + sentinels), `Trivia/` (Trivia, CommentTrivia, CommentKind), `Diagnostics/` (Diagnostic, DiagnosticSeverity, DiagnosticCode, DiagnosticBag), `Lexing/` (TokenKind, Token, LexResult, Lexer). **`Ast/` and `Parsing/` do not exist yet — you create them.**

## What to read, in order

1. **`docs/slices/Slice-2-Parser.md`** — your primary spec (exit criteria, deliverables, statement grammar §6, core-expression grammar §7, params/args §8, trivia propagation §9, diagnostics §10, test plan §11).
2. **`docs/AST-Reference.md`** — authoritative node definitions. You implement **all 40 records** + `IAstVisitor<TResult>` + `AstNode.Accept` now (§3–§9, §12, §13, §17). The comprehension/functional records (ForComprehension, …, LetExpression, AssertExpression, EchoExpression, FunctionLiteral) are **defined now but only parsed in Slice 3**.
3. **`docs/Parser-Planning.md`** — the authoritative operator precedence / binding-power table (translated from `parser.y`). Drive precedence climbing from it. Pin the gotchas: `^` is right-assoc and binds tighter than unary minus; ternary right-assoc; `&` tighter than `|`, both looser than `+`.
4. **`docs/Test-Corpus.md`** — Slice 2 cases `P-001`..`P-003` and expression cases `E-001`..`E-008` (E-009..E-012 are Slice 3). §4 says the on-disk **AST golden format (`expected.ast`) is finalized in Slice 2** — you design a deterministic serialization isomorphic to the §14 notation.
5. **`docs/Diagnostics.md`** — you implement parser codes **SB2001–SB2007** (already cataloged).
6. Ground truth: OpenSCAD `C:\git\hub\openscad\src\core\parser.y` (grammar/precedence). Verify, don't guess.

## How to consume the lexer (the Slice 1 → Slice 2 seam)

- Entry point: `LexResult Lexer.Lex(SourceFile source)` → `LexResult(IReadOnlyList<Token> Tokens, IReadOnlyList<Diagnostic> Diagnostics)`. The stream **always ends with exactly one `Eof`** token. Build `Parser.Parse(SourceFile, IReadOnlyList<Token>)` per Slice-2 §4 and merge lexer diagnostics first (source order).
- `Token` is a `readonly record struct`: `Kind`, `Text` (raw lexeme / `RawText`), `Span`, `LeadingTrivia`, `TrailingTrivia`, `BlankLineBefore`, `NumberValue` (`double?`), `StringValue` (`string?`).
  - `Number` → `NumberLiteral(token.NumberValue!.Value, token.Text)`. `String` → `StringLiteral(token.StringValue!, token.Text)`.
- **Trivia propagation (Slice-2 §9):** a node's `LeadingTrivia` + `BlankLineBefore` come from its **first** token; its `TrailingTrivia` from its **last** token (usually `;`/`}`/`)`). End-of-file comments live on the **`Eof` token's `LeadingTrivia`** — surface them on `ScadFile` so they aren't dropped.
- **`include`/`use` are two tokens:** `Include`/`Use` followed by a `FilePath` token whose `Text`/`StringValue` is the raw path (no `<>`). Parse them as a pair → `IncludeStatement`/`UseStatement(rawPath)`.

## Slice 2 gotchas (where it's easy to go wrong)

- **`intersection_for` is an `Identifier`, not a keyword** — but `for`/`let`/`assert`/`echo`/`each` ARE keyword tokens (`For`/`Let`/`Assert`/`Echo`/`Each`). So `module_id` name-recognition (Slice-2 §6) must read the name from **either** a keyword token's text **or** an `Identifier`: `For`→`ForStatement`, `Let`→`LetStatement`, `Identifier "intersection_for"`→`IntersectionForStatement`; `Echo`/`Assert`/`Each` tokens, `assign`, `child`/`children`, built-ins, and user names → generic `ModuleInstantiation`. Their named args become `Binding`s.
- **Control flow is dedicated nodes, not `ModuleInstantiation`** (`if`/`for`/`intersection_for`/`let`) — AST §15.2. But `echo`/`assert`/`children`/`assign` at **statement** level ARE `ModuleInstantiation`s.
- **Modifiers `* ! # %`** stack (`#%cube();`) → `IReadOnlyList<InstantiationModifier>` outer→inner. The lexer emits them as ordinary `Star`/`Not`/`Hash`/`Percent`; the parser decides modifier-vs-operator by position.
- **Argument vs named-argument:** `Ident =` (an `Assign` token, **not** `==`) → `Argument(name, value)`; otherwise positional. Same idea for `Parameter` defaults.
- **Child chaining:** after `name(args)`, a following instantiation becomes `Child` (`translate(...) rotate(...) cube(1);`); `{…}`→`BlockStatement` child; `;`→`Child = null`.
- **Slice 2 vs Slice 3 boundary:** a vector element is strictly an `expr` in Slice 2. A `for`/`let`/`each`/`if` token starting a vector element, and the expression forms `let(…) e` / `assert(…) e` / `echo(…) e` / `function(…) e`, are **Slice 3** — define the records, don't parse them yet. Keep such inputs out of Slice 2 tests.
- **`AstNode.Accept`:** §13 requires `abstract TResult Accept<TResult>(IAstVisitor<TResult>)` on the base and an override on all 40 concrete records. Hand-writing is fine; the boilerplate is mechanical (consider a small T4/source-gen only if it pays off — not required).

## Conventions carried over from Slice 1 (so the build stays green)

- **Strict build:** `Directory.Build.props` sets net10.0, nullable, `TreatWarningsAsErrors`, analyzers (`latest-Recommended`), `GenerateDocumentationFile`. Every **public** Core member needs an XML doc comment (CS1591 is an error). The test project sets `GenerateDocumentationFile=false`.
- **Analyzer suppressions already in place (and why):** `.editorconfig` disables **CA1720** (domain token/AST names like `Number`/`String` legitimately match BCL type names) and **IDE0130** (`dotnet_style_namespace_match_folder=false`). `tests/.editorconfig` relaxes **CA1707/CA1822/CA1859/CA1515** for test idioms. Add new suppressions only with a written rationale.
- **Namespace gotcha:** the `Trivia` *type* lives in the **root `ScadBundler.Core`** namespace (folder `Trivia/`), NOT `ScadBundler.Core.Trivia`, because a namespace named `Trivia` clashes with the type (CS0118). Your new AST records go in `ScadBundler.Core.Ast` and parser in `ScadBundler.Core.Parsing` — no clash there.
- **`record struct` + property initializers** need an explicit parameterless ctor (see `Token`). AST nodes are `record` (class), so they're fine; immutable throughout, rebuild with `with`.
- **Reusable test harness:** `tests/.../TestSupport/` has `CorpusLocator` (walks up to `ScadBundler.sln`, then `tests/Corpus/<slice>`), `LexDump` (golden render + line-ending normalize), and `LexHelper`. Build the analogous `AstDump` + `slice2-parser/<id>/{input.scad,expected.ast}` corpus, plus inline xUnit tests, the same way.
- **Watch out for `char + char`** in C# = int addition (bit me in tests). Force string context.

## Spec clarifications folded back during Slice 1 (already in the docs)

- `\x00` decodes to a space; invalid `\u`/`\U` → `U+FFFD` (Slice-1 §7.4).
- `FilePath` text is the **raw, untrimmed** path between `<`/`>`, CR/LF stripped (Slice-1 §7.6).
- Test-Corpus `L-` cases use the final `TokenKind` enum names.

If you find a genuine spec gap/ambiguity in Slice 2, **fix the spec too** (don't silently improvise) — this project's whole point is one-shot, spec-driven implementation.

## Non-negotiables (Constitution)

- Hand-written recursive descent + precedence climbing — **no parser generators / ANTLR / regex** in the core path. Immutable AST records. **Collect diagnostics, never throw** (panic-mode recovery to a sync point: `;`, `}`, `Eof`, or a statement-start token). **≥95% line coverage** of `Parsing/`. No runtime interop with OpenSCAD's C++ (reference/fixtures only).

## Definition of Done (Slice-2 §13)

Full AST hierarchy + visitor compiles; `Parser.Parse` never throws; every statement form and ordinary expression parses correctly; `E-001`..`E-008`, `P-001`..`P-003`, and AST-Reference §14.1–14.5/14.8 parse to the documented trees; SB2001–SB2007 each fire + recover; build/tests green zero-warning; `Parsing/` coverage ≥95%.

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~ParserExpressionTests"
dotnet test --collect:"XPlat Code Coverage"     # cobertura under TestResults/
```

## Workflow / repo conventions

- Commits go on the current branch (`Claude_implementation`), **conventional commits**, ending with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Commit when a unit is done; don't push unless asked.
- `.gitattributes` now forces **LF** everywhere (`* text=auto eol=lf`) — the old CRLF-churn warnings are gone.
- User memory (auto-loaded) holds durable conventions: diagnostic scheme, deprecation policy, AST decisions, the C++ source location.

## After Slice 2

Slice 3 (`docs/slices/Slice-3-Parser-Expressions.md`) extends this parser with the functional sublanguage: list-comprehension generators inside `[…]` and the `let/assert/echo/function` expression forms (records already defined here). Pipeline: `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter`.
