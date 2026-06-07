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

## Deprecated Constructs (parse faithfully)
The parser **accepts** deprecated syntax without complaint; normalization and warnings happen downstream (semantic/inliner), not in the parser:
- `assign(bindings) child` → parse to `AssignStatement` (later rewritten to `let`, SB5001).
- `child` / `child(n)` → ordinary `ModuleInstantiation` named `child` (later rewritten to `children`, SB5002).
- `.x/.y/.z` member access → parse `.` + identifier into `MemberExpression` with `string Member`; the parser does **not** reject other identifiers — the semantic pass validates (SB3001).

This keeps the parser permissive and the diagnostics precise. See [Spec.md](Spec.md) and [Diagnostics.md](Diagnostics.md).

## Error Handling Strategy
- Collect diagnostics instead of throwing early.
- Context-aware messages.

## Slice Integration
- Slice 2 will implement core rules based on RapCAD BNF.