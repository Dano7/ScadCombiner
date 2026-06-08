# Handoff — Start Here (Slice 1 Implementation)

> **✅ Slice 1 is complete** (lexer implemented; `dotnet build` zero-warning, `dotnet test` green, `Lexing/` line coverage ≈98%). The notes below are retained as the record of how Slice 1 was approached. **If you are starting a new session, your job is Slice 2** — read [docs/slices/Slice-2-Parser.md](docs/slices/Slice-2-Parser.md) and build the AST + recursive-descent parser on top of `LexResult.Tokens`. Two small spec clarifications were folded back during Slice 1: the `\x00`→space / invalid-`\u`→`U+FFFD` decoding notes in Slice-1 §7.4, the raw (untrimmed) FILEPATH text in §7.6, and the Test-Corpus `L-` cases now use the final `TokenKind` names.

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). The documentation phase is done; you're starting the **first implementation slice**.

## Current state (read this first)

- **Slice 0.5 (documentation) is complete.** All six implementation slices are fully specified under `docs/slices/`, and the cross-cutting reference docs are locked and mutually consistent.
- **There is no implementation code yet.** The repo is docs-only. You are creating the solution from scratch.
- **Your job this session: implement Slice 1 — project setup + the lexer.**
- The project's whole point is *one-shot, spec-driven implementation*. The spec is meant to be enough. If you find a genuine gap/ambiguity, fix the spec too (don't silently improvise).

## What to read, in order

1. **`docs/slices/Slice-1-Lexer.md`** — your primary spec. Exit criteria, project layout, public API, lexing rules, diagnostics, test plan. Implement to this.
2. **`docs/AST-Reference.md` §2–§3** — the foundational types you create in Slice 1: `SourceFile`, `SourcePosition`, `SourceSpan` (+ `Synthetic` sentinels), `Trivia`/`CommentTrivia`/`CommentKind`, and the trivia/`BlankLineBefore` model. (§17 has the full Core file layout.)
3. **`docs/Diagnostics.md`** — the `SBnnnn` scheme; you implement the lexer codes **SB1001–SB1009**.
4. **`docs/Test-Corpus.md` §Slice 1** — golden cases `L-001`..`L-004` plus the §4 notation conventions; turn the §10 test plan into xUnit tests.
5. **`docs/Constitution.md`** — the non-negotiables (below).

## Ground truth (don't guess OpenSCAD behavior)

The official OpenSCAD C++ source is checked out locally at **`C:\git\hub\openscad`** (`openscad-2019.05-3933-g6b81cb63e`). For the lexer, the authority is **`src/core/lexer.l`**. Verify against it rather than memory. `examples/` and `tests/data/` hold real `.scad` fixtures.

## Setup (Slice 1 §3–§4)

Create the solution and the two projects, with strict build settings:
- `ScadBundler.sln`, `src/ScadBundler.Core/`, `tests/ScadBundler.Core.Tests/` (xUnit + coverlet).
- `Directory.Build.props`: `net10.0`, `LangVersion latest`, `Nullable enable`, **`TreatWarningsAsErrors true`**, `EnforceCodeStyleInBuild true`, analyzers on, `GenerateDocumentationFile true`.
- `.editorconfig`, `.gitignore`. Core has **no third-party dependencies**.
- Folders this slice creates: `Text/`, `Trivia/`, `Diagnostics/`, `Lexing/` (see AST-Reference §17). `Ast/` and the parser arrive in Slice 2 — don't build them now.

## Non-negotiables (Constitution)

- **Zero warnings** (warnings are errors). **Hand-written** scanner only — no ANTLR/regex for tokenizing.
- **Immutable** types; `Token` is a `readonly record struct`.
- **Collect diagnostics, never throw** on malformed input — the lexer always returns a token stream ending in `Eof`.
- **≥95% line coverage** of `Lexing/`. TDD against the corpus.
- No runtime interop with OpenSCAD's C++ (it's reference/fixtures only).

## Slice 1 gotchas (these are where it's easy to go wrong)

- **Keyword set is exactly** `{module, function, if, else, for, let, assert, echo, each, true, false, undef}` + contextual `include`/`use`. **`intersection_for` and all built-in names (`cube`, `translate`, …) are `Identifier`s**, not keywords.
- **Maximal munch**: `2d` lexes as one (deprecated, SB1008) `Identifier`, not `2` then `d`.
- **Numbers** include hex `0x[0-9a-fA-F]+`, plus decimal/fraction/scientific — `.5` and `1.` are both valid. Store the parsed `double` in `NumberValue`; keep the raw lexeme in `Text` (that's the AST's `RawText`).
- **String escapes**: `\n \t \r \\ \" \xHH \u#### \U######`; an unknown `\?` → SB1006 (warn, drop the backslash). Decoded value → `StringValue`, raw (incl. quotes) → `Text`.
- **`$fn` etc. are `Identifier`s** (leading `$` is part of the identifier).
- **`include`/`use` are contextual**: keyword only when the next non-whitespace char is `<`. Then scan the `<...>` path into a `FilePath` token (raw text, no escapes). We **do not open files** here — that's Slice 5.
- **Trivia attachment**: comments → leading trivia of the next token; a **same-line** comment after a token → that token's *trailing* trivia (this is how Customizer `// [0:100]` annotations are preserved). `BlankLineBefore` = true when ≥1 blank line preceded the token. End-of-file comments attach to the `Eof` token.
- `* ! # %` are lexed as ordinary tokens (`Star`/`Not`/`Hash`/`Percent`); the *parser* (Slice 2) decides modifier-vs-operator.

## Definition of Done (Slice 1 §13)

Zero-warning build; green xUnit; `L-001`..`L-004` + the §10 token/number/string/identifier/include-use/trivia/recovery batteries pass; every `TokenKind` produced by a test; each SB1001–SB1009 emitted for its trigger with recovery; `Lexing/` coverage ≥95%. Then Slice 2 can consume `LexResult.Tokens`.

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~LexerTests"
```

## Workflow / repo conventions

- This repo commits docs/code **directly to `main`** (solo, linear history). Commit when a unit is done.
- **Conventional commits**; end commit messages with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer (see prior history).
- On Windows you'll see `LF will be replaced by CRLF` warnings from Git — harmless. (Optional: add a `.gitattributes` with `* text=auto`.)
- User memory (auto-loaded) holds durable conventions: diagnostic scheme, deprecation policy, AST decisions, the C++ source location, and the resolved V2 question.

## Do NOT

- Don't build the parser, AST `Statement`/`Expression` records, or open `include` targets — out of scope for Slice 1.
- Don't add dependencies to Core.
- Don't normalize/transform tokens — the lexer is faithful; transforms are Slices 4–5.

## After Slice 1

Slice 2 (`docs/slices/Slice-2-Parser.md`) builds the AST hierarchy + recursive-descent statement parser + precedence-climbing core-expression parser on top of your token stream. The pipeline is `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter` (see `docs/Design.md`).
