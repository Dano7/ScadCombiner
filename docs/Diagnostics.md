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

### SB3003 — Variable reassigned (last-wins) *(Warning, Semantic)*
- **Trigger**: the same variable is assigned more than once in a scope (including across `include`-merged files).
- **Message**: `Variable '{name}' was assigned on line {first} but is overwritten; the last assignment wins.`
- **Notes**: Mirrors OpenSCAD (`parser.y` `handle_assignment`). Variables are **not** sequential — the last assignment wins for the whole scope regardless of where the variable is read. See [Spec.md](Spec.md) "Definition & Variable Collisions".

### SB3004 — Definition redefined (last-wins) *(Warning, Semantic)*
- **Trigger**: a module or function name is defined more than once in the same (merged) scope.
- **Message**: `{module|function} '{name}' is redefined; the last definition wins.`
- **Notes**: OpenSCAD is silent here (`LocalScope.cc` overwrites the lookup entry); we warn. Under `--on-collision rename`/`prefix` the definitions are instead kept and renamed — see Spec collision strategy.

### SB4001 — Include/use file not found *(Warning, Loader)*
- **Trigger**: a `<path>` cannot be resolved on the search path (Spec "File Resolution").
- **Message**: `Can't find '{path}' on the search path; statement ignored.`

### SB4002 — Circular include/use detected *(Error, Loader)*
- **Trigger**: a file appears in its own include/use ancestry.
- **Message**: `Circular reference: '{path}' is already being processed.`
- **Notes**: OpenSCAD silently skips the recursive include (`parsersettings.cc` `check_valid` rejects already-open files); we report it. Fixtures: `tests/data/modulecache-tests/circular*` in the OpenSCAD repo.

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
- `SB1xxx`: unterminated string / block comment / `include`/`use` statement, unexpected character. Confirmed from OpenSCAD `lexer.l`: undefined escape sequence (Warning, drops backslash), integer literal "cannot be represented precisely" (Warning), and variable names starting with a digit (Deprecated).
- `SB2xxx`: expected token, unbalanced brackets, malformed parameter/argument list, illegal modifier placement.
- `SB3xxx`: undefined symbol (where decidable — conservative, see [Builtins-Reference.md](Builtins-Reference.md)), arity issues (if in scope). *(Duplicate definition/reassignment now seeded as SB3003/SB3004.)*
- `SB4xxx`: path escapes allowed roots; ambiguous match across library paths. *(File-not-found and cycle now seeded as SB4001/SB4002.)*
- `SB5xxx`: collision resolution actions (rename applied), dedup actions.
- `SB6xxx`: emitter-level fidelity warnings (if any).
