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
- **Bundler rule**: when inlining a `use`d file, emit the used module/function definitions **plus** any top-level constant assignments those definitions transitively reference, treated as *private constants* of that library (subject to collision prefixing). Top-level geometry and unreferenced top-level variables are dropped. This preserves the original runtime behavior of the imported definitions. (Subtle free-variable cases are confirmed by integration test **V2** — see [AST-Reference.md](AST-Reference.md) §16.)

*Example.* Given `lib.scad`:
```scad
$fn = 64;                 // top-level special var
WALL = 2;                 // top-level constant
module box() cube(WALL);  // references WALL
cube(99);                 // top-level geometry
```
- `include <lib.scad>` → brings in `$fn=64;`, `WALL=2;`, `box()`, **and** emits `cube(99);`.
- `use <lib.scad>` → brings in `box()` and (because `box` references it) `WALL=2;` as a private constant. **Drops** `$fn=64;` and `cube(99);`.

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
