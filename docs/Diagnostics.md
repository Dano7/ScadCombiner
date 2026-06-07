# ScadBundler Diagnostics Catalog

**Status**: Seed. This file establishes the diagnostic **code scheme** and records the diagnostics introduced by resolved design decisions so far. It is **not yet complete** — the full per-slice catalog (every lexer/parser/semantic error) is a dedicated Slice 0.5 task. Add entries here as each slice is specified; never invent a code at implementation time without recording it here first.

## Code Scheme

Format: `SB` + 4 digits. The first digit groups by pipeline phase:

| Range | Phase | Producer |
|---|---|---|
| `SB1xxx` | Lexing | Lexer |
| `SB2xxx` | Parsing | Parser |
| `SB3xxx` | Semantic analysis | SemanticAnalyzer |
| `SB4xxx` | Source loading / path resolution | SourceLoader |
| `SB5xxx` | Inlining / transformation | Inliner |
| `SB6xxx` | Emitting | Emitter |

## Severity Levels

- **Error** — output is not produced (or only with `--force`, if ever offered). Indicates invalid/ambiguous input.
- **Warning** — output IS produced; something was changed or is risky. Deprecation normalizations are warnings.
- **Info** — purely informational; surfaced under `--verbose`.

Every diagnostic carries a `SourceSpan` (file + line/column) and renders as:
`<severity> <code>: <message>  (<file>:<line>:<col>)`.

## Catalog (seeded)

### SB3001 — Invalid vector member access *(Error, Semantic)*
A `MemberExpression` uses a component outside `{x, y, z}`.
- **Trigger**: `v.w`, `v.foo`, etc.
- **Message**: `Invalid member '.{name}'; only .x, .y, and .z are valid vector components.`
- **Notes**: Kept as a semantic error (not a parse error) so the message can point precisely at the offending member and recovery can continue. See [AST-Reference.md](AST-Reference.md) §6 / §15.11.

### SB3002 — Comprehension generator outside vector *(Error, Semantic)*
A list-comprehension generator (`for` / `if` / `let` / `each` in their comprehension forms) appears outside a `VectorExpression`.
- **Trigger**: `x = each [1,2];` or `y = for (i=[0:2]) i;` (not wrapped in `[ ]`).
- **Message**: `'{keyword}' generator is only valid inside a list comprehension '[ ... ]'.`

### SB5001 — Deprecated `assign` normalized to `let` *(Warning, Inliner)*
- **Trigger**: an `AssignStatement` in the input.
- **Action**: rewritten to an equivalent `LetStatement` (bindings preserved verbatim).
- **Message**: `'assign' is deprecated; rewritten to 'let'. (Behavior preserved.)`
- **Verification**: integration test V3.

### SB5002 — Deprecated `child` normalized to `children` *(Warning, Inliner)*
- **Trigger**: a module instantiation named `child`.
- **Action**: `child()` → `children(0)`; `child(n)` → `children(n)`.
- **Message**: `'child(...)' is deprecated; rewritten to 'children(...)'.`
- **Notes**: `child()` with no args means the *first* child, hence `children(0)` — **not** `children()`. Verification: integration test V1.

### SB5003 — Deprecated built-in preserved *(Info, Inliner)*
- **Trigger**: a call to a deprecated built-in whose rewrite could change geometry or file I/O: `import_stl`, `import_dxf`, `import_off`, `dxf_linear_extrude`, `dxf_rotate_extrude`.
- **Action**: **preserved verbatim** (the bundler combines files; it does not refactor model behavior).
- **Message**: `'{name}' is deprecated in OpenSCAD; preserved unchanged. Consider migrating to its modern equivalent.`

## To Be Cataloged (later Slice 0.5 work)
- `SB1xxx`: unterminated string/block comment, invalid number, unexpected character.
- `SB2xxx`: expected token, unbalanced brackets, malformed parameter/argument list, illegal modifier placement.
- `SB3xxx`: duplicate definition in one scope, undefined symbol (where decidable), arity issues (if in scope).
- `SB4xxx`: file not found on search path, include/use cycle, path escapes allowed roots.
- `SB5xxx`: collision resolution actions (rename applied), dedup actions.
- `SB6xxx`: emitter-level fidelity warnings (if any).
