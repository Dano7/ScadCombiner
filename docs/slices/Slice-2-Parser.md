# Slice 2 — Parser: Statements & Core Expressions

**Status**: Implementation-ready. Self-contained with [AST-Reference.md](../AST-Reference.md) (node definitions), [Parser-Planning.md](../Parser-Planning.md) (precedence/binding powers), [Diagnostics.md](../Diagnostics.md) (codes), and [Test-Corpus.md](../Test-Corpus.md) (Slice 2 cases). Consumes `LexResult.Tokens` from [Slice 1](Slice-1-Lexer.md). Ground truth: OpenSCAD `src/core/parser.y` (`openscad-2019.05-3933`).

**Outcome**: a hand-written recursive-descent parser that turns the token stream into an immutable AST `ScadFile`, covering the full **statement** grammar and **ordinary expressions** (the whole precedence cascade, unary, ternary, postfix, primary, vectors, ranges), with comment trivia propagated onto nodes and diagnostics collected with panic-mode recovery.

---

## 1. Exit Criteria

- [ ] The complete AST hierarchy from [AST-Reference.md](../AST-Reference.md) (all 40 records + `IAstVisitor<T>` + `Accept`) compiles in `ScadBundler.Core/Ast/`.
- [ ] `Parser.Parse(LexResult)` returns a `ScadFile` + diagnostics; it **never throws** on malformed input (panic-mode recovery, §10).
- [ ] Every statement form in §6 parses to the correct node, including modifiers, children chaining, `if`/`else if`/`else`, and the name-recognized `for`/`intersection_for`/`let` statements.
- [ ] Every ordinary expression in §7 parses with correct precedence/associativity per [Parser-Planning.md](../Parser-Planning.md) — verified by Test-Corpus `E-001`..`E-008`.
- [ ] All AST-Reference §14 worked examples (14.1–14.5, 14.8) and Test-Corpus `P-001`..`P-003` parse to the documented trees.
- [ ] Comment trivia and `BlankLineBefore` are propagated from tokens onto nodes per §9 (Customizer annotation on `x = 5; // [0:10]` lands on the `AssignmentStatement`).
- [ ] Each diagnostic in §10 (SB2001–SB2007) fires for its trigger and recovery continues.
- [ ] Line coverage of `Parsing/` ≥ 95%.

---

## 2. Scope

**In:** the full AST record hierarchy + visitor; parser infrastructure (token cursor, lookahead, expect/consume, recovery); the **statement** grammar; **ordinary expressions** — ternary, all binary operators, unary, exponent, postfix (call/index/member), primary (literals/identifier/parenthesized), vectors (plain elements), ranges; parameters & arguments.

**Out (→ [Slice 3](Slice-3-Parser-Expressions.md)):** the *functional sublanguage* — list-comprehension generators inside `[…]` (`for`, C-style `for(;;)`, `if`/`else`, `let`, `each`), and the keyword-prefixed **expression forms** `let(…) e`, `assert(…) e`, `echo(…) e`, and anonymous `function(…) e`. The AST records for these are **defined** in Slice 2 (so the hierarchy/visitor compile) but their **parsing** lands in Slice 3.

> Boundary rule: in Slice 2 a vector element is exactly an `expr`; `let`/`assert`/`echo`/`function` are handled only in **statement** position (LetStatement; echo/assert as ModuleInstantiation), not as value-producing expressions. Inputs exercising the deferred forms belong to Slice 3 tests.

---

## 3. Deliverables

```
src/ScadBundler.Core/
  Ast/                     # complete hierarchy (AST-Reference §17 layout)
    AstNode.cs             # base + Accept (defined in Slice 1? no — added here)
    ScadFile.cs
    Statements.cs          # all Statement records
    Expressions.cs         # all Expression records (incl. comprehension records, parsed in Slice 3)
    Support.cs             # Parameter, Argument, Binding
    Enums.cs               # InstantiationModifier, Unary/BinaryOperator
    IAstVisitor.cs
  Parsing/
    Parser.cs              # recursive-descent + precedence climbing
    ParseResult.cs         # (ScadFile Root, IReadOnlyList<Diagnostic> Diagnostics)
    TokenCursor.cs         # position, Peek/Lookahead/Advance/Match/Expect (internal)
tests/ScadBundler.Core.Tests/
  Parsing/
    ParserStatementTests.cs
    ParserExpressionTests.cs    # precedence battery E-001..E-008
    ParserModuleInstantiationTests.cs
    ParserTriviaTests.cs
    ParserRecoveryTests.cs
```

> `Text/`, `Trivia/`, `Diagnostics/`, `Lexing/` already exist from Slice 1. `AstNode`/visitor are introduced here (Slice 1 only needed `Text/` and `Trivia/`).

---

## 4. Public API

```csharp
namespace ScadBundler.Core.Parsing;

public sealed record ParseResult(ScadFile Root, IReadOnlyList<Diagnostic> Diagnostics);

public sealed class Parser
{
    /// Parses a token stream (the Lexer's output) into a ScadFile.
    /// Never throws; syntax errors are reported via Diagnostics and recovered from.
    public static ParseResult Parse(SourceFile source, IReadOnlyList<Token> tokens);

    /// Convenience: lex + parse.
    public static ParseResult Parse(SourceFile source);
}
```

> The parser merges its diagnostics with the lexer's (lexer diagnostics first, in source order). `Parse(SourceFile)` runs `Lexer.Lex` then parses.

---

## 5. AST hierarchy

Define **all** records exactly as specified in [AST-Reference.md](../AST-Reference.md) §3–§9 (base `AstNode` with `Span`/`LeadingTrivia`/`TrailingTrivia`/`BlankLineBefore`; `Statement`/`Expression` abstract; the 40 concrete records; `Parameter`/`Argument`/`Binding`; enums) plus `IAstVisitor<TResult>` and `AstNode.Accept`. The comprehension records and `LetExpression`/`AssertExpression`/`EchoExpression`/`FunctionLiteral` are defined now but constructed only in Slice 3.

---

## 6. Statement grammar

Top level: parse a sequence of statements until `Eof` → `ScadFile(Source, Statements)`. Dispatch by the current token:

| Leading token | Production | Node |
|---|---|---|
| `Use` `FilePath` | use statement | `UseStatement(rawPath)` |
| `Include` `FilePath` | include statement | `IncludeStatement(rawPath)` |
| `Module` `Ident` `(` params `)` statement | module definition | `ModuleDefinition(name, params, body)` |
| `Function` `Ident` `(` params `)` `=` expr `;` | function definition | `FunctionDefinition(name, params, body)` |
| `{` … `}` | block | `BlockStatement(stmts)` |
| `;` | empty | `EmptyStatement()` |
| `If` | if/else (see below) | `IfStatement(cond, then, else?)` |
| `* ! # %` (one or more) | modifier-prefixed instantiation | see modifiers |
| `Ident`/`For`/`Let`/`Assert`/`Echo`/`Each` then `=` | assignment (only `Ident =`) | `AssignmentStatement(name, value)` |
| `Ident`/`For`/`Let`/… then `(` | module instantiation | see below |

**Module definition** body is a *single* statement (`module a() cube(1);` or `module a() { … }`).
**Function definition** body is an `expr` terminated by `;`.

**Module instantiation** (`single_module_instantiation child_statement`):
- `module_id` is `Ident` **or** one of the keyword tokens `For`/`Let`/`Assert`/`Echo`/`Each` used as a name (grammar `module_id`).
- Parse `name ( arguments )`, then a **child_statement**:
  - `;` → `Child = null`
  - `{ … }` → `Child = BlockStatement`
  - another module instantiation → `Child = that node` (this is how chains like `translate(...) rotate(...) cube(...);` nest)
- **Name recognition** (per AST-Reference decision §15.2): if `name == "for"` → `ForStatement(bindings, body)`; `name == "intersection_for"` → `IntersectionForStatement(...)`; `name == "let"` → `LetStatement(...)`. The call's **named arguments become `Binding`s** (`for (i = [0:9])` → `Binding("i", range)`). Everything else (`echo`, `assert`, `children`, `group`, `assign`, user modules, built-ins) → generic `ModuleInstantiation`.

**Modifiers** `* ! # %` (may stack, e.g. `#%cube();`): collect them left-to-right into `IReadOnlyList<InstantiationModifier>` (`*`→Disable, `!`→Root, `#`→Highlight, `%`→Background), then parse the instantiation and attach. Order = outer→inner as written.

**if/else** (`if_statement` / `ifelse_statement`): `if ( expr ) child` then optionally `else child`. `else` binds to the nearest `if` (dangling-else). `else if` ⇒ the else-branch is another `IfStatement`.

**Block restriction (permissive)**: the grammar distinguishes a statement-level block (`inner_input`, may contain definitions) from a module-call child block (`child_statements`, only assignments + instantiations). The parser produces `BlockStatement` for both; the "no definitions inside a child block" restriction is a later semantic concern, not a parse error.

> `assign` is **not** a keyword here — `assign(...)` parses as a generic `ModuleInstantiation` named `assign`; the Slice 5 normalizer rewrites it to `let` (SB5001). Same for `child`/`children` (SB5002). See [AST-Reference.md](../AST-Reference.md) §5.

---

## 7. Core expression grammar

Implement via **precedence climbing** using the binding-power table in [Parser-Planning.md](../Parser-Planning.md). Levels covered in Slice 2:

- **Ternary** `cond ? then : else` (right-assoc; condition is the climbing result at `||`-level) → `ConditionalExpression`.
- **Binary cascade** (left-assoc unless noted): `||`, `&&`, `==`/`!=`, `<`/`<=`/`>`/`>=`, `|`, `&`, `<<`/`>>`, `+`/`-`, `*`/`/`/`%` → `BinaryExpression`. **`^`** is right-assoc and binds tighter than unary → `BinaryExpression(Power)`.
- **Unary prefix** `-`/`+`/`!`/`~` (right-assoc, stackable) → `UnaryExpression` (`+` kept as identity per AST §15.7).
- **Postfix** (left-assoc, tightest): `f(args)` → `FunctionCallExpression`; `e[i]` → `IndexExpression`; `e.x` → `MemberExpression` (member kept as `string`, validated in Slice 4).
- **Primary**: `Number`→`NumberLiteral(NumberValue, Text)`; `String`→`StringLiteral(StringValue, Text)`; `True`/`False`→`BooleanLiteral`; `Undef`→`UndefLiteral`; `Ident`→`Identifier`; `( expr )`→`ParenthesizedExpression`; vectors & ranges below.
- **Vector / range** after `[`:
  - `]` → empty `VectorExpression([])`.
  - parse first `expr`; then if `:` → `RangeExpression(start, step?, end)` (`[a:b]` or `[a:b:c]`); else a `VectorExpression` of comma-separated `expr` (optional trailing comma).

> In Slice 2 a vector element is strictly an `expr`. A `for`/`let`/`each`/`if` token at the start of a vector element is a **Slice 3** feature; such inputs are out of Slice 2's test scope.

---

## 8. Parameters & arguments

- **parameters** (defs): comma-separated, optional trailing comma. Each: `Ident` → `Parameter(name, null)`; `Ident = expr` → `Parameter(name, default)`.
- **arguments** (calls): comma-separated, optional trailing comma. Each: `Ident = expr` (when an `Ident` is immediately followed by `=`, not `==`) → `Argument(name, value)`; otherwise `expr` → `Argument(null, value)`.

---

## 9. Trivia propagation

The parser re-homes token trivia onto AST nodes (the lexer attached trivia to tokens in Slice 1 §8):
- A node's `LeadingTrivia` and `BlankLineBefore` come from its **first** token.
- A node's `TrailingTrivia` comes from its **last** token (typically the closing `;`, `}`, or `)`). This is what carries a Customizer inline annotation: `diameter = 20; // [5:50]` → the trailing trivia of `;` becomes the `AssignmentStatement`'s `TrailingTrivia`.
- Leading trivia attached to the `Eof` token (e.g. a trailing license/section comment at end of file) is preserved on `ScadFile` (e.g. as trailing trivia of the root or the last statement) — do not drop it.
- **No double-attachment** (Slice-2 refinement): when a node shares its first or last token with a child (e.g. a `BinaryExpression` begins with its left operand's first token; a chained `ModuleInstantiation` ends with its child's `;`), the trivia attaches to exactly **one** node so the emitter never emits a comment twice. The rule: leading trivia goes to the *outermost* node that owns its own first token (keywords/names/operators/brackets it consumed itself), trailing trivia to the node that consumed the terminator directly. In practice this means **statements** and atom-bounded expressions (literals, identifiers, `(…)`, `[…]`) carry leading/trailing trivia, while operator/postfix/composite expressions (binary, conditional, call, index, member) carry **span only** — their leading lives on the leftmost leaf and trailing on the rightmost leaf. Comments strictly *inside* operator expressions are therefore rare and out of the Customizer model's scope.

---

## 10. Error recovery & diagnostics

**Panic-mode recovery**: on an unexpected token, emit the diagnostic, then skip tokens until a **synchronization point** — `;`, `}`, `Eof`, or a statement-start token (`Module`, `Function`, `If`, `Use`, `Include`, or a modifier) — and resume. Always terminate; always return a (possibly partial) `ScadFile`. Collect every diagnostic.

| Code | Sev | Trigger | Message |
|---|---|---|---|
| SB2001 | Error | a specific expected token is missing | `Expected '{expected}' but found '{found}'.` |
| SB2002 | Error | token valid nowhere here / unexpected EOF | `Unexpected {token}.` |
| SB2003 | Error | `(`/`[`/`{` with no matching close | `Unclosed '{open}'; expected '{close}'.` |
| SB2004 | Error | statement/def not terminated by `;` | `Missing ';' after {construct}.` |
| SB2005 | Error | expression expected, none found | `Expected an expression.` |
| SB2006 | Error | malformed parameter list | `Invalid parameter list.` |
| SB2007 | Error | malformed argument list | `Invalid argument list.` |

Mirror these into [Diagnostics.md](../Diagnostics.md) (SB2xxx range).

---

## 11. Test plan

- **Statements**: each row of §6 — use/include, module def (single-statement body **and** block body), function def, assignment, empty `;`, block. Modifiers incl. stacking (`P-001`). `for`/`intersection_for`/`let` recognized into dedicated nodes with `Binding`s. `echo`/`assert`/`children`/`assign`/user calls → `ModuleInstantiation`. Child chaining (`translate(...) rotate(...) cube(...);`) and block children. `if`/`else if`/`else` (`AST-Reference` §14.8).
- **Expressions**: Test-Corpus `E-001`..`E-008` (precedence/associativity, incl. `^` right-assoc & vs unary, `&`/`|` ordering). Postfix chains (`a.x[0](1)`), nested parens, empty vector, range `[0:2:10]` (`P-002`).
- **AST worked examples**: AST-Reference §14.1–14.5 parse to the documented trees.
- **Trivia**: leading comment on a statement; trailing same-line comment on `;` → node `TrailingTrivia`; `BlankLineBefore` (`P-003`); end-of-file comment preserved.
- **Recovery**: missing `;` (SB2004), unclosed `(`/`[`/`{` (SB2003), garbage token mid-file → all expected diagnostics emitted and parsing continues to `Eof`.
- **No-throw**: fuzz/representative malformed inputs never throw.

> On-disk AST golden format (`expected.ast`) is finalized here — a deterministic, readable serialization isomorphic to the AST-Reference §14 notation (Test-Corpus §4).

---

## 12. Worked example

Input:
```scad
for (i = [0:2:10]) translate([i, 0, 0]) cube(1);
```
```
ForStatement {
  Bindings = [ Binding { Name="i", Value=RangeExpression {
    Start=NumberLiteral 0, Step=NumberLiteral 2, End=NumberLiteral 10 } } ],
  Body = ModuleInstantiation { Name="translate", Arguments=[ Argument{ Value=VectorExpression[
           Identifier "i", NumberLiteral 0, NumberLiteral 0] } ],
    Child = ModuleInstantiation { Name="cube", Arguments=[Argument{Value=NumberLiteral 1}], Child=null } }
}
```
Note `for` is recognized by name into `ForStatement`, and its named argument `i = …` becomes a `Binding`.

---

## 13. Definition of Done

All §1 boxes checked; statement + core-expression suites pass; `E-001`..`E-008`, `P-001`..`P-003`, and AST-Reference §14.1–14.5/14.8 parse to the documented trees; build/tests green with zero warnings; `Parsing/` coverage ≥95%. Slice 3 then extends the same parser with the comprehension/functional-expression productions.
