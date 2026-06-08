# Parser Planning for ScadBundler

## Overall Parser Architecture
- Hand-written recursive descent.
- Precedence climbing / Pratt parser for expressions.
- Immutable record-based AST.
- Diagnostic collection for errors.

## Key References
- Use Grammar-References.md
- Study BelfrySCAD AST for C# record inspiration.

## AST Node Hierarchy
The complete, authoritative node hierarchy — record names, fields, types, nullability, the visitor interface, and worked examples — lives in **[AST-Reference.md](AST-Reference.md)**. Do not duplicate it here; that document overrides any sketch.

Shape at a glance: `AstNode` → `Statement` / `Expression` (both `abstract record`), with supporting nodes `Parameter`, `Argument`, `Binding`, and `Trivia`. All concrete nodes are `sealed record`.

## Operator Precedence & Associativity

**Source of truth**: OpenSCAD's own Bison grammar `src/core/parser.y`, verified against local checkout `openscad-2019.05-3933-g6b81cb63e`. OpenSCAD encodes precedence as a **stratified grammar** — one non-terminal per level (`logic_or → logic_and → equality → comparison → binaryor → binaryand → shift → addition → multiplication → unary → exponent → call → primary`) — rather than `%left`/`%right` directives. That is unambiguous by construction; the table below is its faithful translation for our precedence-climbing expression parser. The only `%nonassoc` in the grammar is for dangling-`else` (`NO_ELSE`/`TOK_ELSE`), not arithmetic.

From **lowest** binding (loosest) to **highest** (tightest):

| Lvl | Operators | Position | Assoc | AST node |
|----|-----------|----------|-------|----------|
| 1 | `?:` ternary · `let()` `assert()` `echo()` `function()` | at `expr` | right / greedy | ConditionalExpression · LetExpression / AssertExpression / EchoExpression / FunctionLiteral |
| 2 | `\|\|` | binary | left | BinaryExpression(Or) |
| 3 | `&&` | binary | left | BinaryExpression(And) |
| 4 | `==` `!=` | binary | left | BinaryExpression(Equal/NotEqual) |
| 5 | `<` `<=` `>` `>=` | binary | left | BinaryExpression(Less/LessEqual/Greater/GreaterEqual) |
| 6 | `\|` | binary | left | BinaryExpression(BitwiseOr) |
| 7 | `&` | binary | left | BinaryExpression(BitwiseAnd) |
| 8 | `<<` `>>` | binary | left | BinaryExpression(ShiftLeft/ShiftRight) |
| 9 | `+` `-` | binary | left | BinaryExpression(Add/Subtract) |
| 10 | `*` `/` `%` | binary | left | BinaryExpression(Multiply/Divide/Modulo) |
| 11 | `-` `+` `!` `~` | unary prefix | right | UnaryExpression(Negate/Plus/Not/BitwiseNot) |
| 12 | `^` | binary | **right** | BinaryExpression(Power) |
| 13 | `()` `[]` `.` | postfix | left | FunctionCallExpression / IndexExpression / MemberExpression |
| 14 | literals · identifiers · `( … )` · `[ … ]` | primary | — | Literals / Identifier / Vector / Range / ParenthesizedExpression |

### Gotchas (must-encode — each has a case in [Test-Corpus.md](Test-Corpus.md) Slice 3)
1. **`^` binds tighter than unary minus**: `-x^2` = `-(x^2)`, not `(-x)^2`. In the grammar `exponent` sits *below* `unary`, and `unary : '-' unary | … | exponent`.
2. **`^` is right-associative**: `a^b^c` = `a^(b^c)`. Its left operand is a `call` (tighter), its right operand is a `unary` (looser) — so only the right side recurses.
3. **`^`'s right operand may be unary**: `2^-1` = `2^(-1)` is valid.
4. **Unary is right-associative / stackable**: `!!x` and `- -x` are valid.
5. **Ternary condition is only a `logic_or`**: `a ? b : c ? d : e` = `a ? b : (c ? d : e)` (right-assoc, because the else-branch is a full `expr`).
6. **Bitwise `&`/`|` sit between shift and comparison**, with `&` tighter than `|`, and both looser than arithmetic — *unlike C*. Follow the table, not intuition.
7. **Unary `+` is identity** (`'+' unary` returns its operand). We keep `UnaryOperator.Plus` for round-trip fidelity, but it has no runtime effect.
8. **Negative literals**: OpenSCAD folds `-2` into a literal; we represent it as `UnaryExpression(Negate, NumberLiteral 2)` — semantically identical, and keeps the lexer emitting only non-negative numbers.

### Precedence-climbing binding powers
Left binding power (higher = tighter). Parse the right operand of a **left**-assoc operator with `minBp = lbp + 1`; of the **right**-assoc `^` with `minBp = lbp`:

```
||                       10   (left)
&&                       20   (left)
== !=                    30   (left)
< <= > >=                40   (left)
|                        50   (left)
&                        60   (left)
<< >>                    70   (left)
+ -                      80   (left)
* / %                    90   (left)
unary - + ! ~            95   (prefix, right)   // between * / % and ^
^                       100   (right)
() [] .  (postfix)      110   (left)
```

Ternary and the `let`/`assert`/`echo`/`function` prefixes are handled at the `expr` entry point, *outside* the binary climbing loop:
- If the first token is `let`/`assert`/`echo`/`function`, parse that prefix form; its body is a full `expr` consumed greedily to the right. (`assert`/`echo` bodies are optional — grammar `expr_or_empty` — so their AST `Body` is nullable.)
- Otherwise parse the condition via the climbing loop at `minBp = 10` (`||` and tighter). If the next token is `?`, consume `expr` (full) `:` `expr` (full) into a right-associative `ConditionalExpression`.

> **Lexer note** (`src/core/lexer.l`): multi-char operator tokens are `<= >= == != && || << >>`; every other operator is a single character returned as its char code. Numbers: hex `0x[0-9a-fA-F]+`, plus decimal/fraction/scientific (`.5` and `1.` are both valid). String escapes: `\n \t \r \\ \" \xHH \u#### \U######`; an unrecognized escape warns and drops the backslash. `include <…>` is resolved by the lexer (file switch); `use <…>` yields a token — but our AST-based bundler models **both** as statements (we never do textual inclusion).

## Deprecated Constructs (parse faithfully)
The parser **accepts** deprecated syntax without complaint; normalization and warnings happen downstream (semantic/inliner), not in the parser:
- `assign(bindings) child` → parses as a generic `ModuleInstantiation` named `assign` (modern OpenSCAD removed the `assign` keyword); the normalizer later rewrites it to `let` (SB5001), the same mechanism as `child`→`children`. No dedicated AST node.
- `child` / `child(n)` → ordinary `ModuleInstantiation` named `child` (later rewritten to `children`, SB5002).
- `.x/.y/.z` member access → parse `.` + identifier into `MemberExpression` with `string Member`; the parser does **not** reject other identifiers — the semantic pass validates (SB3001).

This keeps the parser permissive and the diagnostics precise. See [Spec.md](Spec.md) and [Diagnostics.md](Diagnostics.md).

## Error Handling Strategy
- Collect diagnostics instead of throwing early.
- Context-aware messages.

## Slice Integration
- Slice 2 will implement core rules based on RapCAD BNF.