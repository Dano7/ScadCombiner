# Slice 3 — Parser: Comprehensions & Functional Expressions

**Status**: Implementation-ready. **Extends the [Slice 2](Slice-2-Parser.md) parser** — all AST records already exist (defined in Slice 2); this slice adds only parsing logic + tests. Self-contained with [AST-Reference.md](../AST-Reference.md) §6–§7 (the records & the comprehension model), [Parser-Planning.md](../Parser-Planning.md) (precedence; `expr`-level forms), and [Test-Corpus.md](../Test-Corpus.md). Ground truth: OpenSCAD `parser.y` productions `expr`, `list_comprehension_elements`, `vector_element`.

**Outcome**: the parser now accepts **all** of OpenSCAD's expression grammar — the list-comprehension sublanguage inside `[ … ]` and the keyword-prefixed expression forms — so any valid `.scad` file parses to a complete AST.

---

## 1. Exit Criteria

- [ ] At `expr` level, these parse: `function (params) e` → `FunctionLiteral`; `let (args) e` → `LetExpression`; `assert (args) e?` → `AssertExpression` (Body nullable); `echo (args) e?` → `EchoExpression` (Body nullable).
- [ ] Inside `[ … ]`, vector elements accept comprehension generators: `for (…) body` → `ForComprehension`; C-style `for (init; cond; update) body` → `ForCComprehension`; `if (c) a [else b]` → `IfComprehension`; `each v` → `EachExpression`; `let (…) gen` → `LetComprehension`.
- [ ] The **trailing-`let` rule** is correct: `[let(a=1) a]` parses the element as a `LetExpression` (an expr), while `[let(a=1) for(…) …]` is a `LetComprehension` (§5).
- [ ] Nested/chained comprehensions parse (`[for(i=…) for(j=…) i*j]`, `[for(i=…) if(c) i]`).
- [ ] `assert`/`echo` with no trailing body parse with `Body = null` (grammar `expr_or_empty`).
- [ ] Test-Corpus `14.6`-style comprehensions and the new `E-009`..`E-012` (§9) parse to the documented trees; OpenSCAD `examples/Functions/list_comprehensions.scad` parses without error.
- [ ] After this slice the parser handles every construct in the OpenSCAD corpus; `Parsing/` coverage remains ≥ 95%.

---

## 2. Scope

**In:** the four `expr`-level keyword-prefixed forms (function literal, `let`/`assert`/`echo` expressions) and the five vector-context comprehension generators (`for`, C-style `for`, `if`/`else`, `let`, `each`), including the `let` ambiguity resolution and `expr_or_empty` bodies.

**Out:** nothing parser-related remains after this slice. (Semantic validation of comprehension *position* is SB3002 in [Slice 4](Slice-4-Semantic.md); the parser only *constructs* generators in vector position.)

---

## 3. Deliverables

Extends `src/ScadBundler.Core/Parsing/Parser.cs` (no new public API — same `Parser.Parse`). New tests:
```
tests/ScadBundler.Core.Tests/Parsing/
  ParserComprehensionTests.cs
  ParserFunctionalExprTests.cs   # function literal, let/assert/echo expressions
```

---

## 4. `expr`-level forms

At the `expr` entry point (`ParseExpr`), **before** falling through to the Slice 2 ternary/binary cascade, dispatch on the leading token (these are right-greedy — the body extends as far right as possible, per grammar):

| Leading | Production | Node |
|---|---|---|
| `Function` | `function ( parameters ) expr` | `FunctionLiteral(parameters, body)` |
| `Let` | `let ( arguments ) expr` | `LetExpression(bindings, body)` — args→`Binding`s |
| `Assert` | `assert ( arguments ) expr_or_empty` | `AssertExpression(args, body?)` |
| `Echo` | `echo ( arguments ) expr_or_empty` | `EchoExpression(args, body?)` |
| otherwise | (Slice 2) | ternary / cascade / primary |

- **Parameters** (function literal) parse exactly like a `function`/`module` def's parameter list (Slice 2 §8): `Ident` or `Ident = default`.
- **`expr_or_empty`**: the body is present iff the next token can start an expression (first-set: `Number`,`String`,`True`,`False`,`Undef`,`Identifier`, `(`, `[`, unary `-`/`+`/`!`/`~`, or a keyword-prefix `let`/`assert`/`echo`/`function`). Otherwise (`;`, `,`, `)`, `]`, `}`, `Eof`) → `Body = null`.
- `if` is **not** an `expr`-level form (OpenSCAD has no if-expression — use ternary). A leading `If` where an expression is expected is a parse error (SB2002), except in the vector-comprehension context (§5).

---

## 5. Vector-element comprehension generators

The Slice 2 vector parser parsed each element as `ParseExpr`. Upgrade it to call **`ParseVectorElement`**:

```
ParseVectorElement():
  switch peek:
    For   -> ParseForComprehension()          # for or C-style (§6)
    Each  -> Each '...'      -> EachExpression(ParseVectorElement())
    If    -> If '(' expr ')' ParseVectorElement()  [Else ParseVectorElement()]?  -> IfComprehension(cond, then, else?)
    Let   -> Let '(' arguments ')' body=ParseVectorElement()      # ambiguity, below
             body is a generator?  -> LetComprehension(bindings, body)
             else (body is an expr) -> LetExpression(bindings, body)
    '(' followed by For/Each/If -> ParseParenthesizedGenerator()  # '(' generator ')', below
    _     -> ParseExpr()                        # §4 forms + Slice 2 cascade
```

Ranges (`[a:b]`/`[a:b:c]`) are still detected first by the vector/range parser (Slice 2 §7) before element parsing; comprehension generators only apply to non-range `[ … ]` contents.

**Bodies are recursive vector elements**, enabling chaining: `for(i) for(j) e` → `ForComprehension(body=ForComprehension(body=e))`; `for(i) if(c) e` → `ForComprehension(body=IfComprehension(…))`.

**The trailing-`let` rule** (grammar: *"the last set element may not be a `let`, as that would be parsed as an expression"*): after `let (args)`, parse the body as a vector element; if the body is a comprehension generator (`For`/`ForC`/`If`/`Let`/`Each` node) → **`LetComprehension`**; if it's an ordinary expression → **`LetExpression`**. Thus `[let(a=1) a]` → `VectorExpression[ LetExpression ]`, but `[let(a=1) for(i=…) a]` → `VectorExpression[ LetComprehension ]`.

**Parenthesized generators**: `( generator )` inside a comprehension body is grouping — parse and wrap as `ParenthesizedExpression(generator)` (preserves the author's parens; generators are `Expression`s in our model). A `(` is taken as a parenthesized generator only when it is immediately followed by `For`/`Each`/`If` (tokens that can never begin an `expr`); a `(` followed by anything else — including `let` and `function` — stays on the ordinary expression path so postfix application still binds (e.g. `(function (x) x)(5)`). One consequence: the trailing-`let` test (`IsComprehensionGenerator`) unwraps a single `ParenthesizedExpression` layer, so `[let(n) (for(i=…) …)]` is still a `LetComprehension`. A *parenthesized* `let`-comprehension as a top-level element (`[ (let(a=1) for(…) …) ]`) is the lone construct this lookahead does not reach — vanishingly rare, and a semantic error anyway when it surfaces outside a generator body.

---

## 6. `for` vs C-style `for`

After `For` `(`, parse an argument-list → bindings `A`, then:
- if next is `;` → **C-style**: consume `;`, parse `expr` → `Condition`, consume `;`, parse argument-list → bindings `U`, consume `)`, parse body → `ForCComprehension(Init=A, Condition, Update=U, Body=body)`.
- else → consume `)`, parse body → `ForComprehension(Bindings=A, Body=body)`.

C-style `for` exists **only** in comprehensions (there is no statement-level C-style `for`).

---

## 7. Notes

- `let`/`assert`/`echo` are keyword tokens (Slice 1) and are also handled in **statement** position by Slice 2 (`LetStatement`; echo/assert as `ModuleInstantiation`). This slice adds only their **expression** forms; context (statement vs expression vs vector-element) selects which.
- Arguments for `let` (→ `Binding`s) and for `assert`/`echo` (→ `Argument`s, positional or named) parse via the Slice 2 argument-list parser.

---

## 8. Worked examples

```scad
squares = [for (i = [0:5]) i * i];
grid    = [for (i = [0:2]) for (j = [0:2]) [i, j]];
evens   = [for (i = [0:9]) if (i % 2 == 0) i];
cstyle  = [for (a = 0; a < 5; a = a + 1) a];
scoped  = [let (n = 3) for (i = [0:n]) i];     // LetComprehension
trailing= [let (a = 1) a];                      // LetExpression (trailing-let rule)
dbl     = function (x) x * 2;                    // AssignmentStatement value = FunctionLiteral
flat    = [each [1, 2, 3], 4];                   // EachExpression then 4
checked = assert(n > 0) n;                       // AssertExpression (body = n)
```
Key trees:
```
scoped → VectorExpression[ LetComprehension {
           Bindings=[Binding "n"=3],
           Body=ForComprehension { Bindings=[Binding "i"=Range 0..n], Body=Identifier "i" } } ]
cstyle → VectorExpression[ ForCComprehension {
           Init=[Binding "a"=0], Condition=(a < 5), Update=[Binding "a"=(a+1)], Body=Identifier "a" } ]
dbl    → AssignmentStatement { Name="dbl",
           Value=FunctionLiteral { Parameters=[Parameter "x"], Body=(x * 2) } }
```

---

## 9. Test plan

- **Comprehensions**: `for`, nested `for`/`for`, `for`+`if` (filter) and `if`/`else`, `each`, `let`-comprehension, C-style `for`; the **trailing-`let`** distinction (`LetExpression` vs `LetComprehension`); parenthesized generator.
- **Functional exprs**: function literal (zero/one/many params, defaults), immediately-invoked `(function(x) x)(5)`, `let`/`assert`/`echo` expressions, `assert`/`echo` with empty body (`Body=null`).
- **New corpus cases** (add to [Test-Corpus.md](../Test-Corpus.md) Slice 3): `E-009` let-comprehension vs trailing-let; `E-010` function literal; `E-011` C-style for; `E-012` assert-expression with and without body.
- **Real-world**: OpenSCAD `examples/Functions/list_comprehensions.scad`, `functions.scad`, `recursion.scad` parse without diagnostics.
- **Regression**: all Slice 2 statement/expression tests still pass (vector parsing upgrade is backward-compatible for plain vectors).

---

## 10. Definition of Done

All §1 boxes checked; comprehension + functional-expression suites pass; the trailing-`let` and `expr_or_empty` edge cases are covered; the OpenSCAD `Functions` examples parse clean; build/tests green, zero warnings, `Parsing/` ≥95%. The parser is now **complete** — Slices 4 (semantics) and 5 (inliner) consume full ASTs.
