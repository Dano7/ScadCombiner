# Slice 1 — Project Setup & Lexer

**Status**: Implementation-ready. This is a self-contained, one-shot-implementable spec. An AI assistant should be able to implement it and self-verify from this document plus [AST-Reference.md](../AST-Reference.md) (foundational types), [Diagnostics.md](../Diagnostics.md) (codes), and [Test-Corpus.md](../Test-Corpus.md) (Slice 1 cases). Ground truth for lexical behavior is OpenSCAD's `src/core/lexer.l` (`openscad-2019.05-3933`).

**Outcome**: a buildable .NET 10 solution with zero warnings, and a hand-written lexer that turns OpenSCAD source text into a token stream with precise source spans, attached comment trivia, and collected diagnostics.

---

## 1. Exit Criteria (acceptance checklist)

- [ ] `dotnet build` succeeds with **zero warnings** (warnings-as-errors on).
- [ ] `dotnet test` runs the xUnit suite green.
- [ ] Lexer tokenizes every construct in §6/§7 with correct `Kind`, `Text`, and `Span` (1-based line/col).
- [ ] Every `TokenKind` (§6) is produced by at least one test.
- [ ] All Test-Corpus Slice 1 cases (`L-001`..`L-004`) pass as golden token streams.
- [ ] Comment trivia is attached per §8 (leading vs same-line trailing); `BlankLineBefore` set correctly.
- [ ] Number decoding handles decimal, fraction (`.5`, `1.`), scientific, and hex (`0xFF`); `NumberValue` is the parsed `double`, `Text` preserves the raw lexeme.
- [ ] String decoding handles all escapes in §7.4; `StringValue` is decoded, `Text` is the raw literal incl. quotes.
- [ ] Each diagnostic in §9 (SB1001–SB1009) is emitted for its trigger, and the lexer **recovers** (never throws on bad input).
- [ ] Line coverage of `Lexing/` ≥ 95%.

---

## 2. Scope

**In:** solution/project scaffolding, build/analyzer config, foundational source types (`SourceFile`, `SourcePosition`, `SourceSpan`, `Trivia`, `CommentTrivia` from AST-Reference §2–§3), the diagnostics plumbing, the `Token`/`TokenKind` types, and the `Lexer`.

**Out:** the parser, any AST `Statement`/`Expression` nodes, semantic analysis, file loading/`include` resolution (the lexer captures the include/use path text but does **not** open files). The rest of the `Ast/` records are added in Slice 2.

---

## 3. Deliverables (solution layout)

```
ScadBundler.sln
Directory.Build.props          # shared build settings (§4)
.editorconfig                  # style + analyzer rules (§4)
.gitignore                     # standard VS/.NET ignore
src/
  ScadBundler.Core/
    ScadBundler.Core.csproj
    Text/
      SourceFile.cs            # SourceFile
      SourcePosition.cs        # SourcePosition (readonly record struct)
      SourceSpan.cs            # SourceSpan (+ Synthetic sentinel)
    Trivia/
      Trivia.cs                # Trivia (abstract), CommentTrivia, CommentKind
    Diagnostics/
      Diagnostic.cs            # Diagnostic record
      DiagnosticSeverity.cs    # Error | Warning | Info
      DiagnosticCode.cs        # SB#### constants/enum
      DiagnosticBag.cs         # collecting sink
    Lexing/
      TokenKind.cs             # the enum (§6)
      Token.cs                 # the token (readonly record struct)
      Lexer.cs                 # the scanner (§7)
      LexResult.cs             # (tokens, diagnostics) pair
tests/
  ScadBundler.Core.Tests/
    ScadBundler.Core.Tests.csproj
    Lexing/
      LexerTests.cs            # token-stream tests (§10)
      LexerNumberTests.cs
      LexerStringTests.cs
      LexerTriviaTests.cs
      LexerDiagnosticTests.cs
```

> The `Ast/` folder from AST-Reference §17 is **not** created here beyond `Text/` and `Trivia/` (which the lexer needs). Statement/Expression records arrive in Slice 2. The CLI project (`src/ScadBundler/`) arrives in Slice 6.

---

## 4. Project setup details

**`Directory.Build.props`** (applies to all projects):
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>          <!-- C# 13+ -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-Recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- **Test framework**: xUnit (+ `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`). Coverage via `coverlet.collector`.
- **`.editorconfig`**: enable the standard .NET analyzers and code-style rules as warnings; with warnings-as-errors this enforces the Constitution's "no warnings". File-scoped namespaces, `var` where apparent, expression-bodied members allowed.
- The Core project has **no third-party dependencies** (Constitution: minimal deps).

---

## 5. Public API

```csharp
namespace ScadBundler.Core.Lexing;

public readonly record struct Token
{
    public required TokenKind Kind { get; init; }
    /// Raw source lexeme. For NUMBER/STRING this is the verbatim text (the AST's RawText);
    /// for FILEPATH it is the raw text between < and > (no delimiters).
    public required string Text { get; init; }
    public required SourceSpan Span { get; init; }
    public IReadOnlyList<Trivia> LeadingTrivia { get; init; } = [];
    public IReadOnlyList<Trivia> TrailingTrivia { get; init; } = [];
    public bool BlankLineBefore { get; init; }
    /// Decoded value for NUMBER (parsed double, incl. hex). Null otherwise.
    public double? NumberValue { get; init; }
    /// Decoded value for STRING (escapes resolved). For FILEPATH, the raw path. Null otherwise.
    public string? StringValue { get; init; }
}

public sealed record LexResult(IReadOnlyList<Token> Tokens, IReadOnlyList<Diagnostic> Diagnostics);

public sealed class Lexer
{
    /// Tokenizes the whole file. The returned token list always ends with a single
    /// EOF token. Never throws on malformed input — errors are reported via Diagnostics.
    public static LexResult Lex(SourceFile source);
}
```

```csharp
namespace ScadBundler.Core.Diagnostics;

public enum DiagnosticSeverity { Error, Warning, Info }

public sealed record Diagnostic(
    string Code,                 // e.g. "SB1001"
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan Span);
```

> `Token` is a `readonly record struct` for low allocation; lists default to empty. The lexer collects diagnostics in a `DiagnosticBag` and returns them — it does not throw (Constitution: collect, don't throw early).

---

## 6. TokenKind (complete)

```csharp
public enum TokenKind
{
    // literals & identifiers
    Identifier, Number, String,
    True, False, Undef,                 // true | false | undef

    // keywords (contextual ones noted)
    Module, Function, If, Else, For, Let, Assert, Echo, Each,
    Include, Use,                       // only when followed by '<' (see §7.6)
    FilePath,                           // the <...> path text after Include/Use

    // assignment & punctuation
    Assign,                             // =
    LParen, RParen, LBrace, RBrace, LBracket, RBracket,
    Semicolon, Comma, Colon, Dot, Question,

    // operators
    Plus, Minus, Star, Slash, Percent, Caret,            // + - * / % ^
    Less, LessEqual, Greater, GreaterEqual, Equal, NotEqual,  // < <= > >= == !=
    And, Or, Not,                       // && || !
    Amp, Pipe, Tilde, ShiftLeft, ShiftRight,             // & | ~ << >>
    Hash,                               // #  (highlight modifier)

    Eof
}
```

> **Modifier note**: the statement modifiers `* ! # %` are lexed as `Star`, `Not`, `Hash`, `Percent` — the *parser* decides modifier-vs-operator by position (Slice 2). The lexer does not special-case them.
>
> **Keyword set is exactly** {`module`,`function`,`if`,`else`,`for`,`let`,`assert`,`echo`,`each`,`true`,`false`,`undef`} plus contextual `include`/`use`. Notably **`intersection_for` is NOT a keyword** — it lexes as `Identifier` (it is a built-in module; see [Builtins-Reference.md](../Builtins-Reference.md)). Same for `union`, `cube`, `translate`, etc.

---

## 7. Lexing rules

Driven by OpenSCAD `lexer.l`. Scan one token at a time; longest-match wins where rules overlap (e.g. `2d` → identifier, not `2` then `d`).

### 7.1 Whitespace
- Space (`U+0020`) and tab (`\t`): skip, advance column.
- `\n`: newline — increment line, reset column to 1. Track for `BlankLineBefore` (§8).
- `\r`: ignore (do not advance column).
- Treat **U+00A0** (NO-BREAK SPACE, UTF-8 `C2 A0`) and **U+FEFF** (BOM, UTF-8 `EF BB BF`) as whitespace anywhere.

### 7.2 Comments → trivia (not tokens)
- Line comment `// … <EOL>`: text up to (not including) the newline. Emit a `CommentTrivia(text, CommentKind.Line)`; `text` includes the leading `//`.
- Block comment `/* … */`: may span lines; `text` includes `/*` and `*/`. Unterminated at EOF → **SB1002** (Error); emit what was scanned and stop.
- Comments are attached to tokens per §8.

### 7.3 Numbers (`Number`, `NumberValue` = parsed double, `Text` = raw)
Match (longest first):
- Hex: `0x[0-9a-fA-F]+` → parse as integer, store as `double`. If it cannot be represented precisely → **SB1007** (Warning).
- Scientific/fraction: `D+E`, `D*.D+E?`, `D+.D*E?` where `D=[0-9]`, `E=[Ee][+-]?D+`. So `.5`, `1.`, `1e3`, `1.5e-3`, `1.e10` are all valid.
- Integer: `D+`. If too large to represent precisely → **SB1007** (Warning).

`NumberValue` is the IEEE-754 double; `Text` is the exact lexeme (preserves `1.0` vs `1`, `0xFF`, etc.) for the AST's `RawText`.

### 7.4 Strings (`String`, `StringValue` = decoded, `Text` = raw incl. quotes)
Opening `"` starts a string. Inside:
- Escapes: `\n`→LF, `\t`→TAB, `\r`→CR, `\\`→`\`, `\"`→`"`, `\x` + two hex (`\x[0-7][0-9a-fA-F]`)→byte, `\u` + 4 hex→UTF-8, `\U` + 6 hex→UTF-8.
  - **`\x00` decodes to a space** (`U+0020`), not a NUL — matching OpenSCAD's `lexer.l` (`i == 0 ? ' '`).
  - A `\u`/`\U` escape whose value is not a valid Unicode scalar (e.g. a lone surrogate `\uD800`, or `> U+10FFFF`) decodes to the replacement char `U+FFFD`.
- Any other `\?` → **SB1006** (Warning, "Undefined escape sequence"); drop the backslash, keep the following character.
- Closing `"` ends it. Unterminated at EOL/EOF → **SB1001** (Error); emit the partial string and recover at the newline/EOF.
- `StringValue` is the decoded content; `Text` is the verbatim literal including the surrounding quotes and original escapes.

### 7.5 Identifiers & keywords
- Identifier: `[A-Za-z_$][A-Za-z0-9_]*`. The leading `$` is included (special variables like `$fn` are `Identifier`s — AST-Reference §6).
- Digit-leading identifier `[0-9][A-Za-z0-9_]*` (e.g. `2d`) → `Identifier` + **SB1008** (Warning — deprecation). (Only reached when the number rules don't match the whole lexeme; longest-match makes `2d` an identifier.)
- After scanning an identifier, if the text equals a keyword in the §6 set, emit that keyword kind instead (`true`/`false`/`undef` → `True`/`False`/`Undef`).

### 7.6 `include` / `use` and FILEPATH (contextual)
OpenSCAD treats `include`/`use` specially only when followed by `<`. Replicate:
- When an identifier scans to `include` or `use` **and** the next non-whitespace character is `<` (whitespace, including newlines, may separate them — `lexer.l` uses `include[ \t\r\n]*"<"`): emit `Include`/`Use`, then enter path mode — consume the `<`, capture raw text up to the next `>` as a `FilePath` token. `Text`/`StringValue` = the **raw path text between `<` and `>`** with carriage returns and newlines removed (no escape processing, no trimming of spaces); may contain `/`, `.`, spaces. Consume the `>`.
  - A newline before `>` → **SB1009** (Warning) but keep scanning.
  - EOF before `>` → **SB1003** (Error, "Unterminated include/use statement").
- Otherwise (`include`/`use` not followed by `<`): emit as a plain `Identifier`. (Rare, but matches OpenSCAD.)

> We deliberately diverge from OpenSCAD here: OpenSCAD's lexer *opens* included files inline (textual). We only **tokenize** the path; file resolution/loading is Slice 5 per [Spec.md](../Spec.md) "File Resolution".

### 7.7 Operators & punctuation
Multi-char first (maximal munch): `<=` `>=` `==` `!=` `&&` `||` `<<` `>>`. Then single chars map to: `= + - * / % ^ < > ! ~ & | # ( ) [ ] { } ; , : . ?` per §6.

### 7.8 End of input & invalid characters
- At end of text, emit a single `Eof` token (its span is an empty range at the end; it carries any trailing leading trivia — §8).
- Any other byte/char not matched above (including non-ASCII UTF-8 outside strings/comments) → **SB1004**/**SB1005** (Error); skip one character and continue (recovery).

---

## 8. Source location & trivia attachment

- **Positions**: `SourcePosition(Offset, Line, Column)` — `Offset` 0-based char index; `Line`/`Column` 1-based (AST-Reference §2). A token's `Span` is `[start, end)` over its lexeme (excludes trivia).
- **Leading trivia**: all comments scanned since the previous token (that are not "trailing" of the previous token) attach to the next token's `LeadingTrivia`, in order.
- **Trailing trivia**: a comment on the **same source line** as a just-emitted token, before the next newline, attaches to that token's `TrailingTrivia`. (This is what carries a Customizer inline annotation: `diameter = 20; // [5:50]` → the `// [5:50]` is trailing trivia of `;`.)
- **`BlankLineBefore`**: set `true` on a token when **one or more blank lines** (a line containing only whitespace) occurred between the previous token's line and this token's line.
- **EOF**: any comments/blank lines after the last real token attach to the `Eof` token's `LeadingTrivia` (e.g. a trailing license comment at end of file is never lost).

> Rationale and the downstream model (parser re-homes token trivia onto AST nodes; `BlankLineBefore` instead of a whitespace node) are in AST-Reference §3 and §15.7.

---

## 9. Diagnostics (SB1xxx — lexer)

Mirror these into [Diagnostics.md](../Diagnostics.md). Severity per the catalog; every one recovers (no throw).

| Code | Sev | Trigger | Message |
|---|---|---|---|
| SB1001 | Error | `"` with no closing `"` before EOL/EOF | `Unterminated string literal.` |
| SB1002 | Error | `/*` with no closing `*/` before EOF | `Unterminated block comment.` |
| SB1003 | Error | `include`/`use` `<` with no closing `>` before EOF | `Unterminated include/use statement.` |
| SB1004 | Error | character not matched by any rule | `Unexpected character '{ch}'.` |
| SB1005 | Error | non-ASCII byte outside string/comment | `Non-ASCII character outside string or comment.` |
| SB1006 | Warning | unknown `\?` escape inside a string | `Undefined escape sequence '\{ch}'; backslash ignored.` |
| SB1007 | Warning | integer/hex literal too large for exact double | `Number '{text}' cannot be represented precisely.` |
| SB1008 | Warning | identifier starting with a digit | `Variable names starting with a digit ('{text}') are deprecated.` |
| SB1009 | Warning | newline inside an include/use `<…>` path | `Newline in include/use path is not well-defined.` |

---

## 10. Test plan

Drive the §1 acceptance with these xUnit tests (golden token streams compared by `Kind`+`Text`+`Span`):

- **From Test-Corpus** (must pass verbatim): `L-001` (numbers/operators), `L-002` (special var + line comment trivia), `L-003` (unterminated string → SB1001), `L-004` (hex literal).
- **Token battery**: one assertion per `TokenKind`, including each multi-char operator and each keyword; confirm `intersection_for`/`cube` lex as `Identifier`.
- **Numbers**: `0`, `42`, `1.0`, `.5`, `1.`, `1e3`, `1.5e-3`, `1.e10`, `0xFF`, `0xdeadBEEF`; verify `NumberValue` and raw `Text`. Oversized integer → SB1007.
- **Strings**: empty `""`, all escapes, `\x41`, `é`, `\U01F600`; undefined escape → SB1006; unterminated → SB1001; verify decoded `StringValue` vs raw `Text`.
- **Identifiers/keywords**: `$fn`, `_x`, `abc123`, every keyword; `2d` → Identifier + SB1008.
- **include/use**: `include <a.scad>`, `use <MCAD/gears.scad>`, `use <std.scad>` → `Include/Use` + `FilePath`; `use` not followed by `<` → Identifier; unterminated → SB1003; newline in path → SB1009.
- **Comments/trivia**: leading line+block comments on next token; same-line trailing comment (`x=5; // c`) as trailing trivia; `BlankLineBefore` true across a blank line, false otherwise; end-of-file comment on `Eof`.
- **Spans**: multi-line input — assert line/column of tokens after newlines.
- **Recovery**: a file with several errors yields all expected diagnostics and still terminates with `Eof`.

---

## 11. Worked example

Input:
```scad
x = 0xFF + 1;  // total
```
Token stream (`Kind Text @line:col-col`; trivia noted):
```
Identifier "x"     @1:1
Assign     "="     @1:3
Number     "0xFF"  @1:5     NumberValue=255
Plus       "+"     @1:10
Number     "1"     @1:12    NumberValue=1
Semicolon  ";"     @1:13    TrailingTrivia=[Line "// total"]
Eof                @1:24
```

See Test-Corpus `L-001`..`L-004` for additional golden streams.

---

## 12. Implementation notes

- **Hand-written** scanner only (Constitution: no generators). A single pass over a `ReadOnlySpan<char>` of `SourceFile.Text` with an index; prefer `span`-slicing over per-char string allocation for lexemes (zero-alloc paths where practical).
- Keep one method per lexical category (`ScanNumber`, `ScanString`, `ScanIdentifierOrKeyword`, `ScanCommentTrivia`, …) for testability and ≥95% coverage.
- The lexer owns number/string **decoding** (it knows hex/escapes); the parser just maps `Number`→`NumberLiteral(NumberValue, Text)` and `String`→`StringLiteral(StringValue, Text)` in Slice 2.

---

## 13. Definition of Done

All §1 boxes checked; the four `L-` corpus cases plus the §10 battery pass; build and test are green with zero warnings; `Lexing/` coverage ≥95%. At that point Slice 2 (parser) can consume `LexResult.Tokens` directly.
