# ScadBundler Software Specification

## Functional Requirements
- Parse any valid OpenSCAD file matching official behavior.
- Produce single-file output that renders identically in OpenSCAD.
- Support major libraries: BOSL2, NopSCADlib, dotSCAD, etc.
- Preserve formatting intent and Customizer compatibility.
- Handle `include` vs `use` semantics correctly (see below).
- Aggregate licenses and provide transformation summary.
- Normalize deprecated language constructs where safe (see below).

## `include` vs `use` Semantics

This distinction is the correctness core of the bundler.

**`include <file>`**
- Inlines the file's **entire** top-level content at the point of inclusion: variable assignments, module/function definitions, **and** top-level module instantiations (geometry), all in source order.
- **Transitive**: the included file's own `include`/`use` statements are resolved and inlined recursively.
- Variable semantics follow OpenSCAD: a later top-level assignment of the same name wins; special variables (`$fn`, …) set at top level propagate to the including scope.

**`use <file>`**
- Imports **only** the module and function *definitions* from the file.
- Does **not** import top-level variable assignments, does **not** execute top-level geometry, and does **not** apply special-variable settings made at the used file's top level.
- The used file's own `include`/`use` dependencies are pulled in **only** insofar as the imported modules/functions actually require them.
- **Bundler rule**: when inlining a `use`d file, emit the used module/function definitions **plus** any top-level constant assignments those definitions transitively reference, treated as *private constants* of that library (subject to collision prefixing). Top-level geometry and unreferenced top-level variables are dropped. This preserves the original runtime behavior of the imported definitions. (**Source-confirmed**: `ScopeContext.cc` `FileContext::lookup_local_*` evaluates a used callable in a fresh `FileContext` of *its own* file — so it sees its own constants and the using file cannot override them. Regression-guarded by integration test **V2** — see [AST-Reference.md](AST-Reference.md) §16.)

*Example.* Given `lib.scad`:
```scad
$fn = 64;                 // top-level special var
WALL = 2;                 // top-level constant
module box() cube(WALL);  // references WALL
cube(99);                 // top-level geometry
```
- `include <lib.scad>` → brings in `$fn=64;`, `WALL=2;`, `box()`, **and** emits `cube(99);`.
- `use <lib.scad>` → brings in `box()` and (because `box` references it) `WALL=2;` as a private constant. **Drops** `$fn=64;` and `cube(99);`.

## File Resolution (search path & cycles)

Mirror OpenSCAD's `find_valid_path` (`parsersettings.cc`). For a **relative** `<path>` in `include`/`use`, search in order and take the first existing, non-directory match:
1. The directory of the file **containing the statement**.
2. Each `OPENSCADPATH` entry, in order.
3. The user library directory.
4. The built-in libraries directory.

Absolute paths are used directly. **Cycle guard**: OpenSCAD rejects a path already open in the include chain (it silently skips the recursive include). Not-found → warning (SB4001) and the statement is dropped.

**Bundler**: replicate this order, with `-p`/`OPENSCADPATH` contributing extra paths; implement explicit cycle detection (SB4002) and never infinite-loop. (OpenSCAD test fixtures exist under `tests/data/modulecache-tests/circular*`.)

## Definition & Variable Collisions (last-wins)

From `LocalScope.cc` and `parser.y` `handle_assignment`:
- **Variables are not sequential.** Within a scope, *all* `x = …` assignments are collected and the **last one wins for the entire scope**, regardless of where `x` is read. OpenSCAD warns on overwrite (*"x was assigned on line N but was overwritten"*, SB3003). The bundler must preserve last-wins when merging files.
- **Modules/functions**: a later same-name definition silently overwrites the earlier for lookup (last wins; OpenSCAD emits no warning — we add SB3004 as a courtesy). The `moduleoverload` test fixture exercises this.

### Collision-strategy implication (this reframes `--on-collision`)
- **`include`** merges into one flat scope, so duplicates already collapse to **last-wins** in OpenSCAD. Renaming them would *change* behavior (e.g. a file that deliberately overrides a library module). The correctness-preserving default for include-merged names is therefore **last-wins + warning**; renaming is opt-in.
- **`use`** keeps each library in its own scope with private constants (see `ScopeContext.cc`). Flattening used libraries into one file **requires namespacing/prefixing** of their top-level constants and any colliding definition names — otherwise a using-file variable could wrongly bind inside a library function. This is a **correctness requirement**, not cosmetic.
- **Forced strategies are deliberate divergence, not emulation.** `keep-first` in particular has no OpenSCAD analogue — it exists as a bundle-time *repair*: when the SB3003/SB3004 warnings reveal that a later (often transitively included, third-party) file accidentally stomps a name the model was written against, `keep-first` pins the name to the first definition without editing or reordering sources the user doesn't control. Consequently a `keep-first` variable keeps its first assignment's *original* expression (whereas `auto`/`keep-last` reproduce OpenSCAD's in-place overwrite: last expression, first position), and the output is intentionally **not** equivalent to the original project. Rationale and use cases: [UX.md](UX.md) "Collision Strategies".

> Net: the sensible default is *origin-dependent* — last-wins for `include`, prefix for `use`. [UX.md](UX.md)'s `--on-collision` should reflect this.

## `use` of Fonts

`use <file.ttf>` / `.otf` registers a **font**, not code (`SourceFile::registerUse` special-cases font extensions). A binary font cannot be inlined — preserve such statements **verbatim** and treat them as pass-through dependencies.

## Deprecated Language Feature Policy ("No Half Measures")

Principle: **accept legacy input, emit clean modern output that is semantically identical.** Pure syntax/scope deprecations with exact modern equivalents are rewritten; deprecated built-ins that affect geometry or file I/O are preserved unchanged (the bundler combines files, it does not refactor models).

| Construct | Status | Bundler action | Diagnostic |
|---|---|---|---|
| `assign(a=…) child` | Deprecated (≥2015.03) | Normalize → `let(a=…) child` | SB5001 (Warning) |
| `child()` | Deprecated | Normalize → `children(0)` (first child) | SB5002 (Warning) |
| `child(n)` | Deprecated | Normalize → `children(n)` | SB5002 (Warning) |
| `import_stl` / `import_dxf` / `import_off` | Deprecated | Preserve verbatim | SB5003 (Info) |
| `dxf_linear_extrude` / `dxf_rotate_extrude` | Deprecated | Preserve verbatim | SB5003 (Info) |

Each rewrite is verified for behavioral equivalence against official OpenSCAD (integration tests V1, V3). Codes and messages: [Diagnostics.md](Diagnostics.md).

## Non-Functional Requirements
- Performance: <2 seconds for typical projects.
- Reliability: 100% syntactically valid output.
- Test Coverage: ≥95%.
- Dependencies: Minimal (no ANTLR, no runtime C++ interop).

## Testing Strategy
- Unit tests for lexer, parser rules, semantic passes, emitter.
- Golden-master tests on real-world projects.
- Integration tests (test-only harness) comparing against official OpenSCAD.

## Out of Scope (v1)
- Full semantic type checking beyond collision detection.
- Code formatting beyond pretty-print basics.
- GUI (handled by separate "ScadBundler Live" project).
