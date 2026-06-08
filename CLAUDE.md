# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**ScadBundler** is an AST-based OpenSCAD file bundler that combines multi-file OpenSCAD projects into a single file for upload to platforms like Thingiverse or MakerWorld. This is a C# .NET 10 CLI tool distributed as a NuGet global tool.

**Slices 1–4 are complete** — the solution (`ScadBundler.Core` + `ScadBundler.Core.Tests`) builds with zero warnings (317 tests). Slice 1 delivered the hand-written lexer (≥95% line coverage of `Lexing/`). Slice 2 delivered the full immutable AST hierarchy (`Ast/` — all 40 records + `IAstVisitor<TResult>` + `Accept`) and the recursive-descent + precedence-climbing parser (`Parsing/` — `Parser`, `TokenCursor`, `ParseResult`; panic-mode recovery, SB2001–SB2007, never throws). Slice 3 completed the parser's expression grammar: the list-comprehension sublanguage inside `[ … ]` (`for`, C-style `for`, `if`/`else`, `each`, `let`-comprehension, the trailing-`let` rule) and the keyword-prefixed `expr` forms (`function` literal, `let`/`assert`/`echo` expressions with `expr_or_empty` bodies). The parser is now **complete**. Slice 4 delivered the **`SemanticAnalyzer`** (`Semantics/` — `SemanticAnalyzer`, `ISemanticModel`/`SemanticModel`, `Symbol`, `Builtins`; 100% line coverage) plus the `Loading/` seam types (`LoadGraph`/`LoadedFile`/`IncludeEdge`/`UseEdge`): two-pass symbol-table construction, OpenSCAD-accurate scope resolution (own → built-ins → `use`d libraries, last-`use`-wins; `include`-merge; variables never imported by `use`), within-scope duplicate detection (SB3003/SB3004), validation (SB3001/SB3002/SB3005), and `PrivateConstants` reachability — all collecting diagnostics, never throwing. `Parser.Parse(SourceFile)` lexes then parses; `SemanticAnalyzer.Analyze(LoadGraph)`/`Analyze(ScadFile)` produces the `ISemanticModel`. **The next step is Slice 5 (the `SourceLoader` + `Inliner`)** — see [docs/slices/Slice-5-Loader-Inliner.md](docs/slices/Slice-5-Loader-Inliner.md): recursive `include`/`use` resolution (populating the `Loading/` graph), cross-file collision *resolution*, flattening, dedup, and normalization (SB4001–SB4002, SB5001–SB5005), consuming this slice's `ISemanticModel`.

## Build & Run Commands

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~LexerTests"   # run a single test class
dotnet test --collect:"XPlat Code Coverage"            # coverage (cobertura)
# (CLI project src/ScadBundler arrives in Slice 6)
# dotnet run --project src/ScadBundler -- bundle myproject.scad -o bundled.scad
# dotnet tool install --global ScadBundler             # end-user install
```

## Architecture

ScadBundler follows a compiler pipeline — **no regex/text hacks in the core path**:

```
SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter
```

1. **SourceLoader** — Recursively resolves `include`/`use` statements, handles search paths and cycle detection.
2. **Lexer** — Hand-written token scanner with precise source locations. Zero-allocation paths where possible.
3. **Parser** — Recursive descent with precedence climbing for expressions. Produces immutable AST records.
4. **AST** — Rich typed record hierarchy, designed for the Visitor pattern. Immutable throughout.
5. **SemanticAnalyzer** — Symbol table construction, scope resolution, collision detection between merged files.
6. **Inliner/Transformer** — Flattens dependencies, deduplicates modules/functions, handles renaming on conflict. Content + signature hashing for dedup.
7. **Emitter** — Pretty-printer with configurable indentation/line length/brace style. Must preserve Customizer `/* [ ... ] */` comments and license headers.

## Non-Negotiable Constraints (from [docs/Constitution.md](docs/Constitution.md))

- **No ANTLR4 or parser generators** in the main codebase — hand-written recursive descent only.
- **No runtime interop** with OpenSCAD's C++ parser (test harnesses only, in a separate project).
- **≥95% line coverage** — unit tests per parser rule, semantic pass, and emitter edge case. Integration tests validate against official OpenSCAD.
- **C# 13+ on .NET 10.0** — use records, pattern matching; minimal dependencies.
- **No warnings** — Roslyn analyzers + EditorConfig enforced.
- Output must be **semantically equivalent** to input — correctness over cleverness.

## Key Design Decisions

- **`include` vs `use`**: `include` brings in all definitions AND executes top-level calls; `use` imports only modules/functions. The bundler must replicate this semantic distinction.
- **Collision resolution**: origin-dependent by default — `include` duplicates are last-wins (matches OpenSCAD), `use`-imported names are namespace-prefixed to preserve library isolation; configurable via `--on-collision`.
- **Customizer support**: Special handling for `/* [ Section ] */` comment blocks and `// [min:max:step]` parameter annotations.
- **Web-ready**: Core library should be consumable via WASM/JSON API to power a future "ScadBundler Live" web companion.

## Grammar References

The grammar reference docs are in [docs/Grammar-References.md](docs/Grammar-References.md) and [docs/Parser-Planning.md](docs/Parser-Planning.md). Key references:
- RapCAD `openscad.bnf` — clean BNF starting point
- BelfrySCAD `openscad_parser` — comprehensive PEG grammar with real-world AST patterns
- `tree-sitter-openscad` — modern grammar.js
- [docs/reference/OpenScad/OpenSCAD_User_Manual.pdf](reference/OpenScad/OpenSCAD_User_Manual.pdf) — semantic specification

**Ground-truth source**: the official OpenSCAD C++ source is checked out locally at `C:\git\hub\openscad` (`openscad-2019.05-3933-g6b81cb63e`). Verify semantics there rather than guessing: `src/core/parser.y` (grammar/precedence), `lexer.l` (tokens/numbers/escapes/include-use), `parsersettings.cc` (`find_valid_path` search order), `ScopeContext.cc` (use/include scoping), `LocalScope.cc` (last-wins), `Builtins::init(...)` registrations (built-ins). `examples/` and `tests/data/modulecache-tests/` are ready test fixtures.

Authoritative derived specs: [docs/AST-Reference.md](docs/AST-Reference.md), [docs/Builtins-Reference.md](docs/Builtins-Reference.md), [docs/Diagnostics.md](docs/Diagnostics.md), [docs/Test-Corpus.md](docs/Test-Corpus.md).

## Development Approach

- Incremental slices, each producing a testable milestone (see [docs/Development-Slices.md](docs/Development-Slices.md))
- Test-Driven + Golden Master tests early (capture expected output for regression)
- Conventional commits, PRs require passing tests
- MIT licensed
