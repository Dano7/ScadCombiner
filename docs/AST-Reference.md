# ScadBundler AST Reference

**Status**: Authoritative. This document is the single source of truth for the ScadBundler Abstract Syntax Tree. Where [Parser-Planning.md](Parser-Planning.md) or other docs sketch node shapes, *this* document overrides them.

**Purpose**: Define every AST node — record name, field names, field types, and nullability — precisely enough that an AI assistant can implement the full tree and its visitor in one shot, and that a parser can be written to populate it without further design decisions.

**Grammar basis**: Node shapes follow the OpenSCAD language as described in the [Official Language Manual](https://en.wikibooks.org/wiki/OpenSCAD_User_Manual/The_OpenSCAD_Language) and cross-checked against the references in [Grammar-References.md](Grammar-References.md) (RapCAD BNF, BelfrySCAD PEG, tree-sitter-openscad). Operator precedence is defined in [Parser-Planning.md](Parser-Planning.md), not here.

---

## Table of Contents
1. [Design Principles](#1-design-principles)
2. [Foundational Types](#2-foundational-types)
3. [Base Node & Trivia](#3-base-node--trivia)
4. [Root Node](#4-root-node)
5. [Statements](#5-statements)
6. [Expressions](#6-expressions)
7. [List Comprehensions](#7-list-comprehensions)
8. [Supporting Nodes](#8-supporting-nodes)
9. [Enums](#9-enums)
10. [Polymorphic Keyword Map](#10-polymorphic-keyword-map)
11. [Customizer Representation](#11-customizer-representation)
12. [Concrete Node Index](#12-concrete-node-index)
13. [Visitor Pattern](#13-visitor-pattern)
14. [Worked Examples](#14-worked-examples)
15. [Design Decisions & Rationale](#15-design-decisions--rationale)
16. [Resolved Decisions & Verification](#16-resolved-decisions--required-verification)
17. [Suggested File Layout](#17-suggested-file-layout)

---

## 1. Design Principles

- **Immutable records.** Every node is a C# `record` (or `readonly record struct` for value-like helpers). Transformations produce new trees via `with` expressions; nodes are never mutated.
- **Closed hierarchy.** All node types live in the core library and derive from `AstNode`. The base types (`Statement`, `Expression`) are `abstract record`; concrete leaf nodes are `sealed record`. This makes the set of nodes knowable for exhaustive `switch` and visitor generation.
- **Parse-only tree.** The AST captures *syntax*. It does **not** carry resolved symbols, resolved include paths, types, or dedup decisions. Those live in side tables produced by later passes (semantic analyzer, source loader, inliner), keyed by **reference identity** via `Dictionary<AstNode, T>(ReferenceEqualityComparer.Instance)` (or `ConditionalWeakTable`) — never by value. Records keep value equality for tests; side tables use reference identity so structurally-identical, `with`-rewritten, or synthesized nodes never collide. This keeps the AST pure and reusable.
- **Round-trip fidelity.** Nodes retain enough raw information (raw number text, raw string text, explicit parentheses, comment trivia, and a `BlankLineBefore` marker) that the emitter can reproduce author intent. Pretty-printing style is the emitter's job; *preserving meaning and Customizer/license comments* is the AST's job.
- **Source provenance on every node.** Every node knows its `SourceSpan`, which includes the originating `SourceFile`. After inlining, nodes from many files coexist in one tree; provenance must survive.

---

## 2. Foundational Types

```csharp
/// A loaded source file. One instance per physical file read.
public sealed record SourceFile(string Path, string Text);

/// A position in a source file.
/// Offset is 0-based char index into SourceFile.Text.
/// Line and Column are 1-based for human-facing diagnostics.
public readonly record struct SourcePosition(int Offset, int Line, int Column);

/// A half-open span [Start, End) within a single file.
public readonly record struct SourceSpan(SourceFile File, SourcePosition Start, SourcePosition End);
```

A span never crosses files. Two well-known sentinels handle synthesized content:

```csharp
public sealed record SourceFile(string Path, string Text)
{
    /// Sentinel file for nodes created by transforms with no real origin
    /// (e.g. a bundler-generated header). Path = "<synthesized>", Text = "".
    public static readonly SourceFile Synthesized = new("<synthesized>", "");
}

public readonly record struct SourceSpan(SourceFile File, SourcePosition Start, SourcePosition End)
{
    /// Span for synthesized nodes that have no origin node to borrow from.
    public static readonly SourceSpan Synthetic =
        new(SourceFile.Synthesized, default, default);
}
```

> **Provenance rule** (resolves former Open Question 1): a node created by a transform reuses the `SourceSpan` of the **origin node** it was derived from (so diagnostics point at the real source — e.g. a renamed module points back at the original `module`). Use `SourceSpan.Synthetic` only when there is genuinely no origin. `Span` is therefore always non-null.

---

## 3. Base Node & Trivia

```csharp
/// Base of all AST nodes.
public abstract record AstNode
{
    /// The source range this node covers. Defaults to SourceSpan.Synthetic so a node can be
    /// constructed and then have its real span attached via a `with` expression (the parser's
    /// build-then-attach pattern); it is never null. (Slice 2 chose a defaulted property over
    /// `required` to keep span/trivia attachment in one place; the "always non-null" guarantee
    /// of §2 is preserved by the non-null Synthetic default.)
    public SourceSpan Span { get; init; } = SourceSpan.Synthetic;

    /// Comments attached before this node (e.g. a Customizer label
    /// line, a license header, a section banner). Empty when none.
    public IReadOnlyList<Trivia> LeadingTrivia { get; init; } = [];

    /// Comments attached after this node on the same line
    /// (e.g. a Customizer inline annotation `// [0:100]`). Empty when none.
    public IReadOnlyList<Trivia> TrailingTrivia { get; init; } = [];

    /// True when one or more blank lines preceded this node in the source.
    /// The emitter renders exactly one blank line before the node when set
    /// (honored primarily at statement boundaries; ignored by --minify).
    /// Replaces a dedicated WhitespaceTrivia node — see §15.7.
    public bool BlankLineBefore { get; init; }
}
```

> Positional `record` parameters (e.g. `NumberLiteral(double Value, string RawText)`) define the node's syntactic fields. The base members (`Span`, `LeadingTrivia`, `TrailingTrivia`) are set with an object initializer:
> `new NumberLiteral(1.0, "1") { Span = span }`.

### Trivia

Trivia is non-semantic source text (comments) that the parser attaches to the nearest node so the emitter can reproduce it. Trivia is **not** visited as part of the main tree walk. Blank lines are *not* trivia — they are captured by `AstNode.BlankLineBefore` (§3 base node, §15.7).

```csharp
public abstract record Trivia
{
    public required SourceSpan Span { get; init; }
}

/// A comment. Text is the full raw comment INCLUDING delimiters
/// (`// ...` or `/* ... */`), so it can be re-emitted verbatim.
public sealed record CommentTrivia(string Text, CommentKind Kind) : Trivia;
```

See [§11 Customizer Representation](#11-customizer-representation) for how Customizer metadata is recovered from `CommentTrivia`.

---

## 4. Root Node

```csharp
/// The parsed contents of one .scad file, or the final bundled output.
public sealed record ScadFile(
    SourceFile Source,
    IReadOnlyList<Statement> Statements
) : AstNode;
```

---

## 5. Statements

```csharp
public abstract record Statement : AstNode;
```

### File inclusion

```csharp
/// `include <path>` — pulls in all definitions AND executes the file's
/// top-level statements at this point.
/// RawPath is the text between < and > (no angle brackets), e.g. "MCAD/gears.scad".
public sealed record IncludeStatement(string RawPath) : Statement;

/// `use <path>` — imports only module and function DEFINITIONS from the file;
/// does NOT execute its top-level statements and does NOT propagate that file's
/// own include/use.
public sealed record UseStatement(string RawPath) : Statement;
```

> Path resolution (search paths, `OPENSCADPATH`, cycle detection) is performed by the SourceLoader and recorded in a side table keyed by the statement node. The AST stores only the raw path. See [§15](#15-design-decisions--rationale).

### Definitions

```csharp
/// `module Name(Parameters) Body`. Body is typically a BlockStatement but the
/// grammar permits any single statement.
public sealed record ModuleDefinition(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    Statement Body
) : Statement;

/// `function Name(Parameters) = Body;`
public sealed record FunctionDefinition(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    Expression Body
) : Statement;
```

### Assignment

```csharp
/// `Name = Value;` at file scope or inside a block.
/// At file scope and before the first definition, these are also Customizer
/// parameters (see §11).
public sealed record AssignmentStatement(string Name, Expression Value) : Statement;
```

### Module instantiation (the workhorse)

```csharp
/// A call to a module: `Modifiers Name(Arguments) Child`.
/// Covers built-ins (cube, translate, union, ...), user modules, and the
/// statement forms of `echo(...)`, `assert(...)`, `children(...)`, `group()`.
///
/// Child encodes what follows the `)`:
///   - null                          → terminated by `;`            e.g. `cube(5);`
///   - a single ModuleInstantiation  → chained child                e.g. `translate(...) cube(5);`
///   - a BlockStatement              → braced children              e.g. `union() { a(); b(); }`
///
/// Modifiers are listed outer→inner as written; e.g. `#%cube();` → [Highlight, Background].
public sealed record ModuleInstantiation(
    IReadOnlyList<InstantiationModifier> Modifiers,
    string Name,
    IReadOnlyList<Argument> Arguments,
    Statement? Child
) : Statement;
```

### Blocks & control flow

OpenSCAD's control structures are syntactically module instantiations, but we model them as dedicated nodes because semantic analysis and inlining treat them specially.

```csharp
/// `{ Statements }`
public sealed record BlockStatement(IReadOnlyList<Statement> Statements) : Statement;

/// `if (Condition) Then` with optional `else Else`.
/// Else may itself be an IfStatement to represent `else if`.
public sealed record IfStatement(
    Expression Condition,
    Statement Then,
    Statement? Else
) : Statement;

/// `for (Bindings) Body` — Bindings iterate as a cartesian product when multiple.
public sealed record ForStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;

/// `intersection_for (Bindings) Body`
public sealed record IntersectionForStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;

/// `let (Bindings) Body` — statement form (geometry scope).
public sealed record LetStatement(
    IReadOnlyList<Binding> Bindings,
    Statement Body
) : Statement;
```

### Edge-case statements

```csharp
/// A lone `;`. Retained for fidelity; the emitter MAY elide it.
public sealed record EmptyStatement() : Statement;
```

> **No `AssignStatement` node.** Modern OpenSCAD (`parser.y` 2019.05) **removed** the `assign` keyword — `assign(a = 1) child` now parses as an ordinary `ModuleInstantiation` named `assign` (just like any other call). The deprecation rewrite `assign`→`let` (SB5001) is therefore a **normalizer transform** that recognizes a module call named `assign` (with named arguments → `let` bindings), exactly as `child`→`children` (SB5002) is handled. This keeps the AST lean and grammar-accurate. See [Parser-Planning.md](Parser-Planning.md) and [Spec.md](Spec.md).

---

## 6. Expressions

```csharp
public abstract record Expression : AstNode;
```

### Literals

```csharp
/// RawText preserves the author's lexical form ("1", "1.0", "1e3", ".5")
/// so the emitter can round-trip it; Value is the parsed double.
public sealed record NumberLiteral(double Value, string RawText) : Expression;

/// Value is the decoded string (escapes resolved); RawText includes the
/// surrounding quotes and original escape sequences.
public sealed record StringLiteral(string Value, string RawText) : Expression;

public sealed record BooleanLiteral(bool Value) : Expression;     // true | false

public sealed record UndefLiteral() : Expression;                 // undef
```

### References

```csharp
/// A variable or function/module name reference.
/// Special variables ($fn, $fa, $fs, $t, $children, $preview, $vpr, ...) are
/// Identifiers whose Name begins with '$'. No separate node type.
public sealed record Identifier(string Name) : Expression;
```

### Collections

```csharp
/// `[Elements]`. Elements may include comprehension generators
/// (ForComprehension, IfComprehension, LetComprehension, EachExpression),
/// which are valid ONLY inside a vector context (enforced by the semantic pass).
/// An empty vector `[]` has zero Elements.
public sealed record VectorExpression(IReadOnlyList<Expression> Elements) : Expression;

/// `[Start : End]` (Step null) or `[Start : Step : End]`.
public sealed record RangeExpression(
    Expression Start,
    Expression? Step,
    Expression End
) : Expression;
```

### Operators

```csharp
public sealed record BinaryExpression(
    BinaryOperator Operator,
    Expression Left,
    Expression Right
) : Expression;

/// Prefix unary: -x, +x, !x. Note: `-5` parses as Unary(Negate, NumberLiteral 5),
/// NOT as a negative literal.
public sealed record UnaryExpression(
    UnaryOperator Operator,
    Expression Operand
) : Expression;

/// Ternary `Condition ? Then : Else`.
public sealed record ConditionalExpression(
    Expression Condition,
    Expression Then,
    Expression Else
) : Expression;

/// Author-written grouping `( Inner )`. Retained to preserve intent and to let
/// the emitter avoid recomputing parenthesization in the common case.
public sealed record ParenthesizedExpression(Expression Inner) : Expression;
```

### Access & calls

```csharp
/// `Target[Index]`
public sealed record IndexExpression(Expression Target, Expression Index) : Expression;

/// `Target.Member` — any member name. Kept as `string` (not an enum) so the parser
/// accepts any `.ident`. Validity is a runtime concern (vectors: .x/.y/.z; ranges:
/// .begin/.step/.end; objects: arbitrary members) and is never validated statically.
/// See §15.11.
public sealed record MemberExpression(Expression Target, string Member) : Expression;

/// `Callee(Arguments)`. Callee is usually an Identifier, but may be any
/// expression to support immediately-invoked function literals, e.g.
/// `(function (x) x + 1)(5)`.
public sealed record FunctionCallExpression(
    Expression Callee,
    IReadOnlyList<Argument> Arguments
) : Expression;
```

### Special expression forms

```csharp
/// `let (Bindings) Body` — expression form.
public sealed record LetExpression(
    IReadOnlyList<Binding> Bindings,
    Expression Body
) : Expression;

/// `assert(Arguments) Body` — Body is the expression the assert guards.
/// Arguments are (condition) or (condition, message). Body is OPTIONAL
/// (OpenSCAD grammar `expr_or_empty`): `x = assert(c);` is legal → Body = null.
public sealed record AssertExpression(
    IReadOnlyList<Argument> Arguments,
    Expression? Body
) : Expression;

/// `echo(Arguments) Body` — echoes then evaluates Body. Body is OPTIONAL
/// (OpenSCAD grammar `expr_or_empty`) → Body may be null.
public sealed record EchoExpression(
    IReadOnlyList<Argument> Arguments,
    Expression? Body
) : Expression;

/// Anonymous function literal: `function (Parameters) Body`.
public sealed record FunctionLiteral(
    IReadOnlyList<Parameter> Parameters,
    Expression Body
) : Expression;
```

---

## 7. List Comprehensions

Comprehension generators are modeled as `Expression` subtypes that are only legal as elements of a `VectorExpression`. This unifies the common case (`[1, 2, 3]`) with the generator case (`[1, 2, for (i=[0:3]) i, 5]`) — both are just a `VectorExpression` whose `Elements` happen to include generators. Nesting (`for (a) for (b) expr`) is represented by a generator whose `Body` is another generator.

```csharp
/// `for (Bindings) Body` inside `[...]`. Multiple bindings = cartesian product.
public sealed record ForComprehension(
    IReadOnlyList<Binding> Bindings,
    Expression Body
) : Expression;

/// C-style `for (Init; Condition; Update) Body` inside `[...]`
/// (OpenSCAD grammar `LcForC`), e.g. `[for (i = 0; i < 10; i = i + 1) i]`.
/// Rare but valid; Init/Update are binding lists, Condition is the loop test.
public sealed record ForCComprehension(
    IReadOnlyList<Binding> Init,
    Expression Condition,
    IReadOnlyList<Binding> Update,
    Expression Body
) : Expression;

/// `if (Condition) Then` inside `[...]`, optionally `else Else`.
/// Without Else it acts as a FILTER; with Else it selects between two yields.
public sealed record IfComprehension(
    Expression Condition,
    Expression Then,
    Expression? Else
) : Expression;

/// `let (Bindings) Body` inside `[...]`.
public sealed record LetComprehension(
    IReadOnlyList<Binding> Bindings,
    Expression Body
) : Expression;

/// `each Value` — flattens Value (a list) into the surrounding vector.
public sealed record EachExpression(Expression Value) : Expression;
```

> **Constraint**: `ForComprehension`, `ForCComprehension`, `IfComprehension`, `LetComprehension`, and `EachExpression` are syntactically valid only as direct or nested elements of a `VectorExpression`. The parser accepts them only in that position; the semantic analyzer emits a diagnostic (SB3002) if one appears elsewhere.

---

## 8. Supporting Nodes

These are `AstNode`s (they carry spans and trivia) but are neither statements nor expressions.

```csharp
/// A formal parameter in a module/function/function-literal definition.
/// DefaultValue is null when the parameter has no default.
public sealed record Parameter(string Name, Expression? DefaultValue) : AstNode;

/// An argument in a call. Name is null for positional args, set for named
/// args (`cube(size = 5)`).
public sealed record Argument(string? Name, Expression Value) : AstNode;

/// A `Name = Value` binding used by let/for/assign and their comprehension forms.
/// (Distinct from AssignmentStatement, which is a top-level/block statement.)
public sealed record Binding(string Name, Expression Value) : AstNode;
```

---

## 9. Enums

```csharp
public enum CommentKind { Line, Block }            // // ...   /* ... */

public enum InstantiationModifier
{
    Disable,     // *  — treat subtree as if commented out
    Root,        // !  — render only this subtree
    Highlight,   // #  — render highlighted (debug)
    Background   // %  — render transparent, excluded from geometry
}

public enum UnaryOperator { Negate, Plus, Not, BitwiseNot }    // -  +  !  ~

public enum BinaryOperator
{
    // arithmetic
    Add, Subtract, Multiply, Divide, Modulo, Power,   // +  -  *  /  %  ^
    // comparison
    Less, LessEqual, Greater, GreaterEqual, Equal, NotEqual,  // <  <=  >  >=  ==  !=
    // logical
    And, Or,                                           // &&  ||
    // bitwise & shift (present in OpenSCAD parser.y)
    BitwiseAnd, BitwiseOr, ShiftLeft, ShiftRight       // &  |  <<  >>
}
```

> Precedence and associativity for all operators are defined authoritatively in [Parser-Planning.md](Parser-Planning.md) (translated from OpenSCAD's `parser.y`). Notably `^` (Power) is **right-associative and binds tighter than unary minus**.

---

## 10. Polymorphic Keyword Map

Several OpenSCAD keywords map to different nodes depending on syntactic context. This table is the disambiguation contract for the parser:

| Keyword | Statement context | Expression context | Inside `[ ... ]` (comprehension) |
|---|---|---|---|
| `if`   | `IfStatement` | use ternary `?:` → `ConditionalExpression` | `IfComprehension` |
| `for`  | `ForStatement` | — | `ForComprehension`; C-style → `ForCComprehension` |
| `intersection_for` | `IntersectionForStatement` | — | — |
| `let`  | `LetStatement` | `LetExpression` | `LetComprehension` |
| `each` | — | — | `EachExpression` |
| `assert` | `ModuleInstantiation` (name `assert`) | `AssertExpression` | — |
| `echo`   | `ModuleInstantiation` (name `echo`) | `EchoExpression` | — |
| `function` | `FunctionDefinition` (named) | `FunctionLiteral` (anonymous) | — |
| `assign` | `ModuleInstantiation` (deprecated; normalized → `let`, SB5001) | — | — |

---

## 11. Customizer Representation

The Customizer model is **not** separate AST nodes — it is an *interpretation* of trivia attached to top-level `AssignmentStatement`s. This keeps the parse tree clean while making all Customizer data recoverable.

**Recognition rules** (per the OpenSCAD manual):
- Customizer parameters are the `AssignmentStatement`s in the file's top-level scope that appear **before the first `ModuleDefinition` or `FunctionDefinition`**.
- A **section** is introduced by a block comment `/* [Section Title] */` (a `CommentTrivia` in `LeadingTrivia`). It groups subsequent parameters until the next section comment.
- `/* [Hidden] */` hides all subsequent parameters from the Customizer UI.
- A **label/description** is a line comment immediately above the assignment (`LeadingTrivia`).
- An **inline annotation** is a trailing line comment on the same line as the assignment (`TrailingTrivia`), constraining the control:
  - `// [max]` or `// [min:max]` or `// [min:max:step]` → numeric slider/spinner
  - `// [a, b, c]` → dropdown of values
  - `// [a:Label A, b:Label B]` → labeled dropdown
  - quoted-string value + `// [8]` → max length, etc.

A derived, tooling-facing projection (built by a Customizer pass, **not** part of the core AST) may look like:

```csharp
/// DERIVED, not a parse node. Produced by interpreting trivia on a top-level
/// AssignmentStatement. Lives in the tooling layer for ScadBundler Live.
public sealed record CustomizerParameter(
    string Name,
    Expression DefaultValue,
    string? Section,        // null = default/ungrouped
    string? Description,    // from the label line comment
    string? RawAnnotation,  // the raw `[ ... ]` annotation text, if any
    bool Hidden
);
```

> The bundler MUST preserve all Customizer trivia. With comments preserved (the default) every comment is emitted; under a comment-stripping mode (`--minify`/`--obfuscate`/`--no-preserve-comments`) the comments the Customizer reads off each hoisted parameter — its `/* [Section] */` group header, its description line, and its inline `// [ ... ]` annotation — are marked sticky and survive (only ordinary comments and the long library headers drop), so the bundled model's Customizer is unchanged.

---

## 12. Concrete Node Index

Every concrete node, grouped. This list is exhaustive — it doubles as the set of visitor methods (§13) and the set of types a `switch` must handle.

**Root (1):** `ScadFile`

**Statements (12):** `IncludeStatement`, `UseStatement`, `ModuleDefinition`, `FunctionDefinition`, `AssignmentStatement`, `ModuleInstantiation`, `BlockStatement`, `IfStatement`, `ForStatement`, `IntersectionForStatement`, `LetStatement`, `EmptyStatement`

**Expressions (23):** `NumberLiteral`, `StringLiteral`, `BooleanLiteral`, `UndefLiteral`, `Identifier`, `VectorExpression`, `RangeExpression`, `BinaryExpression`, `UnaryExpression`, `ConditionalExpression`, `ParenthesizedExpression`, `IndexExpression`, `MemberExpression`, `FunctionCallExpression`, `LetExpression`, `AssertExpression`, `EchoExpression`, `FunctionLiteral`, `ForComprehension`, `ForCComprehension`, `IfComprehension`, `LetComprehension`, `EachExpression`

**Supporting (3):** `Parameter`, `Argument`, `Binding`

**Trivia (1):** `CommentTrivia`

> Total concrete node types: **40** (1 root + 12 statements + 23 expressions + 3 supporting + 1 trivia). The five comprehension generators (`ForComprehension`, `ForCComprehension`, `IfComprehension`, `LetComprehension`, `EachExpression`) are counted among expressions.

---

## 13. Visitor Pattern

A generic visitor with one method per concrete node. Because the hierarchy is closed and sealed, this can be hand-written or **source-generated** (preferred — see Constitution's stance on source generators) to stay in sync as nodes are added.

```csharp
public interface IAstVisitor<out TResult>
{
    TResult Visit(ScadFile node);

    // statements
    TResult Visit(IncludeStatement node);
    TResult Visit(UseStatement node);
    TResult Visit(ModuleDefinition node);
    TResult Visit(FunctionDefinition node);
    TResult Visit(AssignmentStatement node);
    TResult Visit(ModuleInstantiation node);
    TResult Visit(BlockStatement node);
    TResult Visit(IfStatement node);
    TResult Visit(ForStatement node);
    TResult Visit(IntersectionForStatement node);
    TResult Visit(LetStatement node);
    TResult Visit(EmptyStatement node);

    // expressions
    TResult Visit(NumberLiteral node);
    TResult Visit(StringLiteral node);
    TResult Visit(BooleanLiteral node);
    TResult Visit(UndefLiteral node);
    TResult Visit(Identifier node);
    TResult Visit(VectorExpression node);
    TResult Visit(RangeExpression node);
    TResult Visit(BinaryExpression node);
    TResult Visit(UnaryExpression node);
    TResult Visit(ConditionalExpression node);
    TResult Visit(ParenthesizedExpression node);
    TResult Visit(IndexExpression node);
    TResult Visit(MemberExpression node);
    TResult Visit(FunctionCallExpression node);
    TResult Visit(LetExpression node);
    TResult Visit(AssertExpression node);
    TResult Visit(EchoExpression node);
    TResult Visit(FunctionLiteral node);
    TResult Visit(ForComprehension node);
    TResult Visit(ForCComprehension node);
    TResult Visit(IfComprehension node);
    TResult Visit(LetComprehension node);
    TResult Visit(EachExpression node);

    // supporting
    TResult Visit(Parameter node);
    TResult Visit(Argument node);
    TResult Visit(Binding node);
}

/// Accept dispatches to the matching Visit overload.
public abstract record AstNode
{
    public abstract TResult Accept<TResult>(IAstVisitor<TResult> visitor);
}
```

> Most transforms (inliner, renamer, minifier) are better written as a **rewriting visitor** that returns `AstNode` and rebuilds changed subtrees with `with`. A `Unit`/`void` variant or a `record`-returning base rewriter should be provided. Exact rewriter base class is a Slice-2/3 implementation detail; the `IAstVisitor<TResult>` contract above is the fixed interface.

---

## 14. Worked Examples

Notation: `NodeName { field = value, ... }`; lists in `[ ... ]`; `Span`/trivia omitted for brevity.

### 14.1 Module instantiation with named arg
```scad
cube([10, 20, 30], center = true);
```
```
ModuleInstantiation {
  Modifiers = [],
  Name = "cube",
  Arguments = [
    Argument { Name = null, Value = VectorExpression { Elements = [
      NumberLiteral { Value=10, RawText="10" },
      NumberLiteral { Value=20, RawText="20" },
      NumberLiteral { Value=30, RawText="30" } ] } },
    Argument { Name = "center", Value = BooleanLiteral { Value = true } }
  ],
  Child = null            // terminated by ';'
}
```

### 14.2 Transform chain (children)
```scad
translate([0, 0, 5]) rotate([0, 0, 45]) cube(10);
```
```
ModuleInstantiation { Name="translate", Arguments=[ Argument{ Value=VectorExpression[0,0,5] } ],
  Child = ModuleInstantiation { Name="rotate", Arguments=[ Argument{ Value=VectorExpression[0,0,45] } ],
    Child = ModuleInstantiation { Name="cube", Arguments=[ Argument{ Value=NumberLiteral 10 } ],
      Child = null } } }
```

### 14.3 Module definition with default + braced body
```scad
module washer(d = 5, h = 2) {
    cylinder(d = d, h = h);
}
```
```
ModuleDefinition {
  Name = "washer",
  Parameters = [
    Parameter { Name="d", DefaultValue = NumberLiteral 5 },
    Parameter { Name="h", DefaultValue = NumberLiteral 2 }
  ],
  Body = BlockStatement { Statements = [
    ModuleInstantiation { Name="cylinder", Arguments=[
      Argument{ Name="d", Value=Identifier "d" },
      Argument{ Name="h", Value=Identifier "h" } ], Child=null }
  ] }
}
```

### 14.4 Function definition with ternary
```scad
function clamp(x, lo, hi) = x < lo ? lo : (x > hi ? hi : x);
```
```
FunctionDefinition {
  Name = "clamp",
  Parameters = [ Parameter{Name="x"}, Parameter{Name="lo"}, Parameter{Name="hi"} ],
  Body = ConditionalExpression {
    Condition = BinaryExpression { Operator=Less, Left=Identifier "x", Right=Identifier "lo" },
    Then = Identifier "lo",
    Else = ParenthesizedExpression { Inner = ConditionalExpression {
      Condition = BinaryExpression { Operator=Greater, Left=Identifier "x", Right=Identifier "hi" },
      Then = Identifier "hi",
      Else = Identifier "x" } }
  }
}
```

### 14.5 include / use
```scad
include <BOSL2/std.scad>
use <helpers.scad>
```
```
IncludeStatement { RawPath = "BOSL2/std.scad" }
UseStatement     { RawPath = "helpers.scad" }
```

### 14.6 List comprehension with filter
```scad
squares = [for (i = [0 : 5]) if (i % 2 == 0) i * i];
```
```
AssignmentStatement {
  Name = "squares",
  Value = VectorExpression { Elements = [
    ForComprehension {
      Bindings = [ Binding { Name="i", Value = RangeExpression {
        Start=NumberLiteral 0, Step=null, End=NumberLiteral 5 } } ],
      Body = IfComprehension {
        Condition = BinaryExpression { Operator=Equal,
          Left = BinaryExpression { Operator=Modulo, Left=Identifier "i", Right=NumberLiteral 2 },
          Right = NumberLiteral 0 },
        Then = BinaryExpression { Operator=Multiply, Left=Identifier "i", Right=Identifier "i" },
        Else = null }            // filter form
    }
  ] }
}
```

### 14.7 Customizer parameter with trivia
```scad
/* [Dimensions] */
// Outer diameter of the part
diameter = 20; // [5:50]
```
```
AssignmentStatement {
  Name = "diameter",
  Value = NumberLiteral { Value=20, RawText="20" },
  LeadingTrivia = [
    CommentTrivia { Kind=Block, Text="/* [Dimensions] */" },
    CommentTrivia { Kind=Line,  Text="// Outer diameter of the part" }
  ],
  TrailingTrivia = [
    CommentTrivia { Kind=Line,  Text="// [5:50]" }
  ]
}
```
The Customizer pass derives:
`CustomizerParameter { Name="diameter", Section="Dimensions", Description="Outer diameter of the part", RawAnnotation="[5:50]", Hidden=false }`.

### 14.8 if / else if / else
```scad
if (n == 0) a();
else if (n == 1) b();
else c();
```
```
IfStatement {
  Condition = BinaryExpression{ Equal, Identifier "n", NumberLiteral 0 },
  Then = ModuleInstantiation{ Name="a", Child=null },
  Else = IfStatement {
    Condition = BinaryExpression{ Equal, Identifier "n", NumberLiteral 1 },
    Then = ModuleInstantiation{ Name="b", Child=null },
    Else = ModuleInstantiation{ Name="c", Child=null }
  }
}
```

---

## 15. Design Decisions & Rationale

These choices are fixed for cross-implementation consistency (one of the AI-comparison goals). Deviations should be raised as Open Questions, not made silently.

1. **Comprehension generators are `Expression` subtypes, legal only inside vectors.** Unifies `[1,2,3]` and `[for(...) ...]` under one `VectorExpression` and makes nesting natural. The alternative (a separate `VectorElement` union) complicates the overwhelmingly common plain-list case.
2. **Control flow (`if`/`for`/`let`) are dedicated statement nodes**, not generic `ModuleInstantiation`s, even though OpenSCAD's grammar treats them as module instantiations. Dedicated nodes give the semantic analyzer and inliner clean, typed access to conditions/bindings.
3. **`echo`/`assert`/`children` as statements ARE `ModuleInstantiation`s** (no dedicated nodes). They behave like ordinary module calls at statement level; their *expression* forms (`echo(...) x`, `assert(...) x`) get dedicated nodes because they wrap a value.
4. **Raw text retained on numbers and strings.** Preserves `1.0` vs `1`, scientific notation, and exact escapes — required for faithful round-tripping and to avoid surprising diffs in bundled output.
5. **`ParenthesizedExpression` is kept** rather than re-derived from precedence. Preserves author intent (a stated value) and lets the emitter avoid a class of precedence bugs. The emitter still inserts parentheses where a transform makes them necessary.
6. **AST is parse-only; resolution lives in reference-keyed side tables.** Include resolution, symbol binding, and dedup decisions are pass outputs stored in `Dictionary<AstNode, T>(ReferenceEqualityComparer.Instance)` (or `ConditionalWeakTable`), never as fields on nodes. Records keep value equality for tests; side tables use *reference* identity, so structurally-identical, `with`-rewritten, or synthesized nodes never collide. Synthetic nodes carry their origin node's `SourceSpan` for diagnostics, or `SourceSpan.Synthetic` when there is no origin.
7. **Comments are trivia; blank lines are a flag; the emitter owns the rest.** Comments (incl. Customizer/license) ride on `LeadingTrivia`/`TrailingTrivia`; a single `AstNode.BlankLineBefore` bool preserves intentional section breaks. All other formatting (indentation, brace style, wrapping) is regenerated by the emitter per its config — this is why we are a *bundler*, not a formatter. (Chosen over a `WhitespaceTrivia` node: leaner, and captures the only whitespace intent that matters.)
8. **`Binding` vs `AssignmentStatement` are distinct types** despite identical shape, because they occupy different grammatical positions (let/for binding vs. statement) and visitors/analyzers treat them differently.
9. **Numbers are `double`.** OpenSCAD has no integer type — every number is an IEEE-754 double — so `double` reproduces its exact arithmetic and precision limits. The lexer accepts hex (`0xFF`), decimal, fraction (`.5`, `1.`), and scientific (`1e3`) forms; all parse to `double`, and very large values lose precision exactly as OpenSCAD warns. Emit fidelity (`1` vs `1.0` vs `1e3` vs `0xFF`) comes from `RawText`, not the numeric type.
10. **Deprecated constructs are handled, not ignored ("No Half Measures").** Pure syntax/scope deprecations with exact modern equivalents are auto-normalized with a warning (`assign`→`let`; `child()`→`children(0)`; `child(n)`→`children(n)`). Deprecated *built-in calls* whose rewrite could alter geometry or file I/O (`import_stl`, `import_dxf`, `import_off`, `dxf_linear_extrude`, `dxf_rotate_extrude`) are preserved verbatim with an info diagnostic — the bundler combines, it does not refactor behavior. Full policy in [Spec.md](Spec.md); codes in [Diagnostics.md](Diagnostics.md).
11. **Member access is accepted, not enum-typed or validated.** `MemberExpression.Member` stays `string` so the parser accepts any `.ident`. OpenSCAD resolves member validity at runtime — vectors expose `.x/.y/.z` (and `.w/.r/.g/.b/.a`/swizzles under the experimental feature), ranges `.begin/.step/.end`, and objects (`textmetrics()`/`fontmetrics()`) arbitrary members; an unmatched member yields `undef`, never a compile-time error. Since the bundler cannot know a value's runtime type, it performs no static member validation (the former SB3001 was retired).

---

## 16. Resolved Decisions & Required Verification

All six former open questions are now **resolved** (see the linked sections for detail):

| # | Question | Resolution | Where |
|---|---|---|---|
| 1 | Synthetic-node spans | Reuse origin node's span; `SourceSpan.Synthetic` sentinel only when no origin. Reference-equality side tables prevent collisions. | §2, §15.6 |
| 2 | Blank-line fidelity | `WhitespaceTrivia` dropped; replaced by `AstNode.BlankLineBefore` bool. | §3, §15.7 |
| 3 | `assign` (and `child`) | Fully supported & normalized: `assign`→`let` (SB5001), `child()`→`children(0)` / `child(n)`→`children(n)` (SB5002). | §5, [Spec.md], [Diagnostics.md] |
| 4 | `use` semantics | Specified precisely; bundler preserves used-file private constants, drops top-level geometry/vars. | [Spec.md] |
| 5 | Number representation | `double` — OpenSCAD has no integer type; `RawText` preserves emit form. Closed. | §15.9 |
| 6 | Member access surface | Any `.ident` accepted; `Member` stays `string`, validity is runtime-only, no static validation (SB3001 retired). | §6, §15.11 |

### Required verification (integration tests vs. official OpenSCAD — test-only harness)

These behaviors are decided above but **must be confirmed** against the reference C++ engine, since the manual is subtle:

- **V1** — `child()` (no args) renders the *first* child, equivalent to `children(0)` (NOT `children()`); `child(n)` ≡ `children(n)`. Validates the SB5002 rewrite.
- **V2** *(resolved from source — `ScopeContext.cc`)* — A `use`d callable evaluates in a fresh `FileContext` of *its own* file, so it **does** see its own top-level constants, and the using file can neither see nor override them. Confirms the inliner's "preserve private constants + namespace on flatten" rule; the integration test is now a regression guard, not an open question.
- **V3** — `assign(...)` ≡ `let(...)` for the binding-preserving rewrite (no sequential dependency in `assign`). Validates the SB5001 rewrite.

New genuine open questions should be appended here as they arise.

---

## 17. Suggested File Layout

The full `ScadBundler.Core` tree, built up across slices (the per-slice docs are authoritative for what each slice creates). The AST records (this document) live under `Ast/`:

```
src/ScadBundler.Core/
  Text/         SourceFile.cs · SourcePosition.cs · SourceSpan.cs          # Slice 1
  Trivia/       Trivia.cs (Trivia, CommentTrivia, CommentKind)            # Slice 1
  Diagnostics/  Diagnostic.cs · DiagnosticSeverity.cs · DiagnosticCode.cs · DiagnosticBag.cs  # Slice 1
  Lexing/       TokenKind.cs · Token.cs · Lexer.cs · LexResult.cs          # Slice 1
  Ast/          AstNode.cs · ScadFile.cs · Statements.cs · Expressions.cs · Support.cs · Enums.cs · IAstVisitor.cs   # Slice 2
  Parsing/      Parser.cs · ParseResult.cs · TokenCursor.cs                # Slices 2–3
  Semantics/    SemanticAnalyzer.cs · ISemanticModel.cs · Symbol.cs        # Slice 4
  Loading/      SourceLoader.cs · LoadGraph.cs                             # Slice 5
  Inlining/     Inliner.cs · Bundler.cs · BundleOptions.cs · BundleResult.cs  # Slice 5
  Emitting/     Emitter.cs · EmitOptions.cs                                # Slice 6
```

> **Note**: source-text primitives (`Text/`) and `Trivia/` are deliberately *outside* `Ast/` — they are not AST nodes. Grouping records by category (not one-file-per-record) keeps the tree navigable and matches the "lean, clean" value. Split only if a file grows unwieldy.

---

*Cross-references: [Constitution.md](Constitution.md) (principles), [Parser-Planning.md](Parser-Planning.md) (precedence, parsing strategy), [Grammar-References.md](Grammar-References.md) (source grammars), [Spec.md](Spec.md) (`include`/`use` semantics), [Development-Slices.md](Development-Slices.md) (slice plan).*
