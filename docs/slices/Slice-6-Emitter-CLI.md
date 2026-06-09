# Slice 6 — Emitter & CLI

**Status**: Implemented (Slices 1–6 complete; pipeline closed end-to-end). The capstone: renders the bundled `ScadFile` (from [Slice 5](Slice-5-Loader-Inliner.md)) to text and ships the `scadbundler` command. Self-contained with [AST-Reference.md](../AST-Reference.md) (nodes, trivia, `RawText`), [Parser-Planning.md](../Parser-Planning.md) (precedence — for parenthesization), [UX.md](../UX.md) (CLI surface), and [Test-Corpus.md](../Test-Corpus.md) (`EM-001`/`EM-002`; this slice makes the `B-*` reference outputs **exact goldens**).

**Outcome**: a deterministic pretty-printer that turns the bundled AST into valid, well-formatted OpenSCAD preserving comments/Customizer blocks/licenses — and a NuGet-distributed CLI that runs the whole pipeline end-to-end.

---

## 1. Exit Criteria

- [x] `Emitter.Emit(scadFile, options)` produces **valid OpenSCAD** for any AST the parser can produce.
- [x] **Idempotent**: `Emit(Parse(Emit(ast))) == Emit(ast)`; and for canonically-formatted input, `Emit(Parse(src)) == src` (`EM-002`).
- [x] **Round-trip fidelity**: `RawText` preserved for numbers/strings; author parentheses preserved; comment trivia (incl. Customizer `/* [..] */` and `// [min:max]`) preserved and correctly placed (`EM-001`); `BlankLineBefore` → exactly one blank line.
- [x] **Correct parenthesization**: synthesized/transformed subtrees get the minimal parentheses needed to preserve meaning per [Parser-Planning.md](../Parser-Planning.md) precedence.
- [x] `--minify` drops comments/blank lines and emits the shortest semantically-equivalent text (necessary token-separating spaces kept).
- [x] The `B-001`..`B-007` reference bundles, emitted with default options, match checked-in `expected.scad` goldens exactly (Test-Corpus §4).
- [x] `scadbundler bundle <in> [opts]` runs load→analyze→inline→emit, writes output (or stdout), prints diagnostics, returns correct exit codes.
- [x] Packs as a .NET global tool (`dotnet tool install --global ScadBundler`; command `scadbundler`).
- [x] Coverage of `Emitting/` ≥ 95% (`Emitter.cs` ≈ 97%); CLI covered by integration tests (`ScadBundler.Cli.Tests`).

---

## 2. Scope

**In:** the `Emitter` (AST→text, deterministic default style, configurable, minify, precedence-aware parens, trivia/license preservation) and the `ScadBundler` CLI project (arg parsing, pipeline wiring, output/diagnostics/exit codes, NuGet tool packaging).

**Out:** the pipeline stages themselves (Slices 1–5, consumed). Web/WASM/JSON API (post-v1, though the core stays consumable for it).

---

## 3. Deliverables

```
src/ScadBundler.Core/Emitting/
  EmitOptions.cs           # IndentWidth/Style, BraceStyle, Minify, PreserveComments, MaxLineLength
  Emitter.cs               # AST -> string (visitor)
src/ScadBundler/           # CLI tool project (deferred here from Slice 1)
  ScadBundler.csproj       # <PackAsTool>true</PackAsTool>, <ToolCommandName>scadbundler</ToolCommandName>, PackageId=ScadBundler
  Program.cs               # entry point + arg parsing + pipeline wiring
  BundleCommand.cs
tests/ScadBundler.Core.Tests/Emitting/
  EmitterTests.cs          # per-node formatting, idempotence (EM-002), trivia (EM-001), minify, parens
tests/ScadBundler.Cli.Tests/
  CliTests.cs              # end-to-end: bundle fixtures, options, exit codes, dry-run/diff/verbose
```
Add `src/ScadBundler` to `ScadBundler.sln`. Core remains dependency-free; the CLI project may use a small arg-parsing dependency (e.g. `System.CommandLine`) or a hand-rolled parser.

---

## 4. Emitter API & options

```csharp
namespace ScadBundler.Core.Emitting;

public enum IndentStyle { Spaces, Tabs }
public enum BraceStyle  { SameLine, NextLine }

public sealed record EmitOptions(
    int IndentWidth = 4,
    IndentStyle IndentStyle = IndentStyle.Spaces,
    BraceStyle BraceStyle = BraceStyle.SameLine,
    int MaxLineLength = 100,     // advisory in v1 (no hard wrapping); reserved for future
    bool Minify = false,
    bool PreserveComments = true)
{
    public static readonly EmitOptions Default = new();
}

public sealed class Emitter
{
    /// Renders a ScadFile to OpenSCAD text. Deterministic for a given AST + options.
    public static string Emit(ScadFile file, EmitOptions? options = null);
}
```

---

## 5. Default formatting (locks the goldens)

Deterministic so `expected.scad` goldens are stable. (All configurable; these are the defaults.)

- **Indent**: 4 spaces per nesting level.
- **One statement per line** at current indent; statement ends with `;` immediately (no space before).
- **Blocks** (`BraceStyle.SameLine`, K&R): `{` follows the header after one space; children indented +1; `}` on its own line at the header's indent. A single-statement body (non-block) is emitted on the same line: `module a() cube(1);`.
- **Module instantiation**: `name(args)` then — `Child == null` → `;`; `Child` is a block → ` { … }`; `Child` is another instantiation (chain) → one space then the child, kept on one line (`translate([0, 0, 5]) rotate([0, 0, 45]) cube(10);`). Modifiers prefix directly: `#`, `%`, `!`, `*` (outer→inner).
- **Keyword forms** (locks the goldens): control-flow and functional keywords are immediately followed by their `(` with **no space** — `if(c)`, `for(i = …)`, `intersection_for(…)`, `let(a = 1)`, `function(x)`, `assert(c)`, `echo(s)` — and a single space separates the `)` from the body/clause (`let(a = 1, b = 2) translate(…) cube(1);`). `else` is surrounded by single spaces; an `if`/`else if`/`else` chain is emitted on one line. `module`/`function` definitions keep the space after the keyword before the name (`module ring(d) …`).
- **Arguments / parameters**: `, `-separated; no space after `(` or before `)`; named as `name = value`.
- **Operators**: binary → ` op ` (spaces both sides); unary prefix → no space (`-x`, `!x`); ternary → `c ? a : b`.
- **Collections**: vectors `[a, b, c]`; ranges `[start:end]` / `[start:step:end]` (no spaces around `:`); index `a[i]`; member `a.x`.
- **Literals**: numbers and strings via `RawText` verbatim (preserves `0xFF`, `1.0`, escapes).
- **Parentheses**: emit an existing `ParenthesizedExpression`; additionally insert parens around any child whose operator precedence is lower than its parent's (or equal on the associativity-sensitive side) so the re-parsed tree is identical — needed after transforms (rename/normalize) that produced synthesized nodes.
- **Comments/trivia**: a node's leading `CommentTrivia` is emitted on its own line(s) at the node's indent; trailing same-line trivia after two spaces (`… ;  // [5:50]`). `BlankLineBefore` → one empty line before the leading trivia. Customizer `/* [Section] */` / `/* [Hidden] */` blocks and `// [min:max:step]` annotations are ordinary trivia — emitted verbatim, preserving Customizer compatibility.
- **License headers**: emitted from whatever leading trivia the inliner placed on the root (Slice 5 `--bundle-licenses` aggregates+dedups them).

**`--minify`**: omit all comments and blank lines; drop optional whitespace; keep only spaces required to separate adjacent tokens (e.g. between two identifiers/numbers or after a keyword). Output must still parse to an equivalent AST.

---

## 6. Emitter design notes

- A visitor over the AST building into a `StringBuilder` with an indent counter; one method per node (mirrors `IAstVisitor`).
- **Self-check (recommended)**: in debug/tests, re-parse the emitted text and assert it round-trips structurally (ignoring spans/trivia). A failure is an internal emitter bug — surface as **SB6001** (Error, "emitted output failed to re-parse"); should never fire in production.
- Precedence/associativity for paren insertion come from [Parser-Planning.md](../Parser-Planning.md) (the same table the parser uses).

---

## 7. CLI

Command (per [UX.md](../UX.md)):
```
scadbundler bundle <input.scad> [options]
```

| Option | Effect |
|---|---|
| `-o, --output <file>` | output path (default `<input>.bundled.scad`; `-` = stdout) |
| `-p, --library-path <p>` | extra search path (repeatable / comma-separated); `OPENSCADPATH` appended → `BundleOptions.LibraryPaths` |
| `--on-collision <s>` | `auto`(default)\|`prefix`\|`error`\|`keep-first`\|`keep-last` → `BundleOptions.OnCollision` |
| `--bundle-licenses` | aggregate license headers → `BundleOptions.BundleLicenses` |
| `--[no-]preserve-comments` | default on; off drops comments → `EmitOptions.PreserveComments` |
| `--minify` | `EmitOptions.Minify = true` (implies comments dropped) |
| `--dry-run` | run everything, write nothing; print summary |
| `--diff` | print a unified diff between the root input and the bundled output |
| `--verbose` | list inlined files, renames, normalizations, and counts |

**Flow**: parse args → build `BundleOptions` + `EmitOptions` → `Bundler.Bundle(input, bundleOpts)` → print diagnostics (grouped by severity, source-ordered) → if no `--dry-run`, `Emitter.Emit(result.Bundled, emitOpts)` → write to output/stdout. `--diff`/`--verbose`/`--dry-run` adjust output as above.

**Exit codes**: `0` success (output produced); `1` one or more **Error**-severity diagnostics (no output written); `2` usage/argument error. Warnings/info never fail the run.

**Packaging**: `PackAsTool=true`, `ToolCommandName=scadbundler`, `PackageId=ScadBundler` → `dotnet tool install --global ScadBundler`.

---

## 8. Diagnostics

No new everyday codes — the CLI surfaces diagnostics from the whole pipeline (SB1xxx–SB5xxx). Reserved: **SB6001** (Error) emitter self-check failure (internal; should never occur). Add SB6001 to [Diagnostics.md](../Diagnostics.md).

---

## 9. Test plan

- **Per-node formatting**: every node kind renders per §5; chains stay one-line; blocks indent; defs with block vs single-statement bodies.
- **`EM-002` idempotence**: for each `slice6-emit` and `slice5-bundle` golden, `Emit(Parse(expected)) == expected`.
- **`EM-001` trivia**: the Customizer example (AST-Reference §14.7) round-trips with section/label/inline annotation intact and correctly placed.
- **Parens**: author parens preserved; a synthesized tree (e.g. after a rename or `assign`→`let`) gets minimal correct parens; precedence gotchas (`-x^2`, `a || b && c`) re-emit unchanged.
- **Minify**: output parses to an equivalent AST; no comments; token-separating spaces preserved (`a b` not `ab`).
- **Goldens**: apply default options to the `B-001`..`B-007` reference bundles → exact `expected.scad`.
- **CLI**: end-to-end bundle of a multi-file fixture to stdout and to a file; each option (`-p`, `--on-collision`, `--minify`, `--bundle-licenses`, `--dry-run`, `--diff`, `--verbose`); exit codes (clean = 0, error input = 1, bad args = 2); `OPENSCADPATH` honored.
- **Integration (test-only)**: emitted bundles for the OpenSCAD `examples/` corpus re-parse clean; optionally render-compare against the official engine (V1–V3 harness).

---

## 10. Worked example

Input project — `lib.scad`: `module ring(d) circle(d);` · `main.scad`:
```scad
include <lib.scad>
ring(5);
```
`scadbundler bundle main.scad -o out.scad` → `out.scad` (default style):
```scad
module ring(d) circle(d);
ring(5);
```
Exit code `0`; with `--verbose`, reports `1 file inlined (lib.scad), 0 renames, 0 normalizations`.

---

## 11. Definition of Done

All §1 boxes checked; emitter is deterministic and idempotent; `EM-001`/`EM-002` and the `B-*` exact goldens pass; `--minify`/parens/trivia/Customizer/license behaviors verified; the CLI runs the full pipeline with correct output, diagnostics, and exit codes, and packs+installs as `scadbundler`. **This completes the pipeline** `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter` end-to-end — and closes Slice 0.5.
