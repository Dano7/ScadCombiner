# ScadBundler Diagnostics Catalog

**Status**: Substantially complete. Establishes the `SBnnnn` **code scheme** and catalogs every diagnostic referenced by the slice specs (SB1xxx–SB6xxx). A few non-essential edge cases remain under "To Be Cataloged", to be added as the relevant slice is implemented. **Never invent a code at implementation time without recording it here first.**

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

### Lexer (SB1xxx)
Full detail and recovery behavior in [slices/Slice-1-Lexer.md](slices/Slice-1-Lexer.md) §9. Every lexer diagnostic recovers (the lexer never throws).

| Code | Sev | Trigger | Message |
|---|---|---|---|
| SB1001 | Error | unterminated string literal | `Unterminated string literal.` |
| SB1002 | Error | unterminated block comment | `Unterminated block comment.` |
| SB1003 | Error | unterminated `include`/`use` (`<` with no `>`) | `Unterminated include/use statement.` |
| SB1004 | Error | unrecognized character | `Unexpected character '{ch}'.` |
| SB1005 | Error | non-ASCII char outside string/comment | `Non-ASCII character outside string or comment.` |
| SB1006 | Warning | unknown string escape `\?` | `Undefined escape sequence '\{ch}'; backslash ignored.` |
| SB1007 | Warning | integer/hex literal too large for exact double | `Number '{text}' cannot be represented precisely.` |
| SB1008 | Warning | identifier starting with a digit | `Variable names starting with a digit ('{text}') are deprecated.` |
| SB1009 | Warning | newline inside an include/use path | `Newline in include/use path is not well-defined.` |

### Parser (SB2xxx)
Full detail in [slices/Slice-2-Parser.md](slices/Slice-2-Parser.md) §10. The parser recovers via panic-mode (skip to a synchronization point) and never throws.

| Code | Sev | Trigger | Message |
|---|---|---|---|
| SB2001 | Error | a specific expected token is missing | `Expected '{expected}' but found '{found}'.` |
| SB2002 | Error | token valid nowhere here / unexpected EOF | `Unexpected {token}.` |
| SB2003 | Error | `(`/`[`/`{` with no matching close | `Unclosed '{open}'; expected '{close}'.` |
| SB2004 | Error | statement/def not terminated by `;` | `Missing ';' after {construct}.` |
| SB2005 | Error | expression expected, none found | `Expected an expression.` |
| SB2006 | Error | malformed parameter list | `Invalid parameter list.` |
| SB2007 | Error | malformed argument list | `Invalid argument list.` |

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
- **Notes**: OpenSCAD is silent here (`LocalScope.cc` overwrites the lookup entry); we warn. Under `--on-collision prefix` the definitions are instead kept and namespaced — see Spec collision strategy.

### SB3005 — Unknown reference *(Warning, Semantic)*
- **Trigger**: a module/function/variable reference that resolves to nothing — not a built-in, special variable, local binding, or any reachable user declaration. Emitted **conservatively** (only when all files are loaded).
- **Message**: `Unknown {module|function|variable} '{name}'.`
- **Notes**: mirrors OpenSCAD's "Ignoring unknown …" warnings; conservative to avoid false positives from library names the analyzer can't see. See [slices/Slice-4-Semantic.md](slices/Slice-4-Semantic.md) §8.

### SB4001 — Include/use file not found *(Warning, Loader)*
- **Trigger**: a `<path>` cannot be resolved on the search path (Spec "File Resolution").
- **Message**: `Can't find '{path}' on the search path; statement ignored.`

### SB4002 — Circular include/use detected *(Error, Loader)*
- **Trigger**: a file appears in its own include/use ancestry.
- **Message**: `Circular reference: '{path}' is already being processed.`
- **Notes**: OpenSCAD silently skips the recursive include (`parsersettings.cc` `check_valid` rejects already-open files); we report it. Fixtures: `tests/data/modulecache-tests/circular*` in the OpenSCAD repo.

### SB5001 — Deprecated `assign` normalized to `let` *(Warning, Inliner)*
- **Trigger**: a module call named `assign` (modern OpenSCAD parses `assign(...)` as an ordinary `ModuleInstantiation`; we recognize it by name).
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

### SB5004 — Name renamed to resolve collision *(Warning, Inliner)*
- **Trigger**: a definition (or its private constant) is renamed/namespaced **and the rename is noteworthy** — i.e. it resolves a genuine cross-file clash: two `use`d libraries (or a `use`d name and a `use`d name) sharing a symbol, a `use`d name clashing with an own/included name, or any origin under `--on-collision prefix`.
- **Action**: rename to `<filestem>__name` + rewrite all bound references.
- **Message**: `'{name}' from '{file}' renamed to '{newname}' to resolve a collision.`
- **Notes**: every `use`-imported symbol is namespaced **by construction** (ADR 0001 — OpenSCAD evaluates a `use`d library in its own `FileContext`, so it is always isolated). When the import does **not** clash with anything, that by-construction namespacing is performed **silently** (no SB5004) — otherwise the code would fire once per library symbol (e.g. for every name in `use <BOSL2/std.scad>`). `include`-origin definitions are never namespaced (flat last-wins) and `$`-special variables are never namespaced (dynamic scope) or imported.

### SB5005 — Duplicate definition deduplicated *(Info, Inliner)*
- **Trigger**: structurally-identical definitions (same kind/name/params/body, ignoring spans & trivia) arrive via multiple include/use paths (e.g. a diamond include).
- **Action**: keep one, drop the rest.
- **Message**: `Duplicate definition '{name}' merged ({n} copies).`

### SB5006 — Collision under `--on-collision error` *(Error, Inliner)*
- **Trigger**: a genuine cross-file name collision (two structurally-distinct definitions of the same kind/name that survive dedup) while the forced `error` strategy is selected.
- **Action**: the whole bundle is failed — no output is produced (the CLI exits `1`). One diagnostic is emitted per colliding site.
- **Message**: `Collision: {module|function|variable} '{name}' is also defined at {file}:{line}; no output is produced under '--on-collision error'.`
- **Notes**: only `--on-collision error` raises this; `auto`/`keep-first`/`keep-last` resolve silently or with SB3003/SB3004 warnings, and `prefix` namespaces via SB5004. A structural duplicate (SB5005) is not a collision and does not trigger it.

### SB5007 — File headers/licenses aggregated *(Info, Inliner)*
- **Trigger**: at least one **non-root** file's leading header/license comments were hoisted into the bundle's aggregated top header block (the default-on `--bundle-licenses` attribution pass).
- **Action**: each file's header run — the leading comments of its first statement (or the EOF comments of a comments-only file), cut at the first Customizer group marker `/* [Name] */` — is **moved** to the top of the bundle in include/use encounter order (root's own header first, unframed; non-root headers in a delimited, labeled block) and deduplicated by normalized text. One-line provenance banners (`// ======== include <lib.scad> ========`, with `(continued)` on re-entry) additionally separate the inlined sections. Fires once per bundle.
- **Message**: `Aggregated {n} file header(s) into the bundle header.`
- **Notes**: a root-only header hoist does not fire this (it is positionally a no-op). `--no-bundle-licenses` disables the whole pass; `--minify`/`--no-preserve-comments` drop the block and banners like any comment. Group markers always stay with the parameter they precede, so the Customizer UI is unaffected.

### SB6001 — Emitter self-check failure *(Error, Emitter — internal)*
- **Trigger**: emitted output fails to re-parse to an equivalent AST (an internal emitter bug; should never occur in production).
- **Message**: `Internal: emitted output failed to re-parse.`
- **Notes**: enabled in debug/tests as a correctness guard. See [slices/Slice-6-Emitter-CLI.md](slices/Slice-6-Emitter-CLI.md) §6.

## To Be Cataloged (add as the relevant slice is implemented)
- `SB3xxx`: arity issues (if statically decidable). *(Duplicate/reassignment = SB3003/SB3004; unknown reference = SB3005.)*
- `SB4xxx`: path escapes allowed roots; ambiguous match across library paths. *(File-not-found and cycle now seeded as SB4001/SB4002.)*
- `SB6xxx`: emitter fidelity warnings (if any). *(Self-check failure seeded as SB6001.)*
