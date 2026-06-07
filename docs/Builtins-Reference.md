# ScadBundler Built-ins Reference

**Status**: Authoritative for the named OpenSCAD version. The list of built-in modules, functions, special variables, and constants below was extracted from the OpenSCAD source (`Builtins::init(...)` registrations) in local checkout **`openscad-2019.05-3933-g6b81cb63e`**.

**Why this matters**: the semantic analyzer (Slice 4) needs to know which names are built-in so it does **not** flag `cube`, `sin`, etc. as undefined, and the collision logic (Slice 4/5) needs to know reserved names. It also informs which calls are "known" vs "user-defined".

> **Version-robustness rule**: the built-in set changes across OpenSCAD versions, and libraries add many names. Therefore the analyzer must treat an unknown call as a **user/library symbol**, not a hard error. "Undefined symbol" diagnostics must be conservative (off or warn-only by default). This list is for *recognition and collision-awareness*, not for rejecting input.

---

## Built-in Modules

| Group | Modules | Source file |
|---|---|---|
| CSG booleans | `union`, `difference`, `intersection` | `CsgOpNode.cc` |
| Transforms | `translate`, `rotate`, `scale`, `mirror`, `multmatrix`, `resize`, `color`, `offset` | `TransformNode.cc`, `CgalAdvNode.cc`, `ColorNode.cc`, `OffsetNode.cc` |
| Hull/CGAL | `hull`, `minkowski`, `fill`, `render` | `CgalAdvNode.cc`, `RenderNode.cc` |
| Extrude / 2D↔3D | `linear_extrude`, `rotate_extrude`, `projection`, `roof`†| `LinearExtrudeNode.cc`, `RotateExtrudeNode.cc`, `ProjectionNode.cc`, `RoofNode.cc` |
| 3D primitives | `cube`, `sphere`, `cylinder`, `polyhedron` | `primitives.cc` |
| 2D primitives | `square`, `circle`, `polygon`, `text` | `primitives.cc`, `TextNode.cc` |
| Import / data | `import`, `surface` | `ImportNode.cc`, `SurfaceNode.cc` |
| Control / meta | `for`, `intersection_for`, `if`, `let`, `echo`, `assert`, `children`, `group` | `control.cc`, `GroupModule.cc` |

† `roof` is gated behind `Feature::ExperimentalRoof` (off by default).

> The control/meta names are *also* grammar keywords (`for`, `if`, `let`, `echo`, `assert`, `each`) — see [AST-Reference.md](AST-Reference.md) §10 for how they parse. `group` is the implicit container for a brace block.

## Built-in Functions

| Group | Functions | Source file |
|---|---|---|
| Math | `abs`, `sign`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `atan2`, `floor`, `ceil`, `round`, `ln`, `log`, `pow`, `sqrt`, `exp`, `min`, `max`, `norm`, `cross`, `rands` | `builtin_functions.cc` |
| String / list / data | `len`, `concat`, `lookup`, `str`, `chr`, `ord`, `search`, `textmetrics`†, `fontmetrics`† | `builtin_functions.cc` |
| Type predicates | `is_undef`, `is_list`, `is_num`, `is_bool`, `is_string`, `is_function`, `is_object`† | `builtin_functions.cc` |
| Meta / version | `version`, `version_num`, `parent_module` | `builtin_functions.cc` |
| Object (experimental)† | `object`, `has_key`, `import` (function form) | `builtin_functions.cc` |
| DXF (when built with DXF) | `dxf_dim`, `dxf_cross` | `io/dxfdim.cc` |

† Gated behind a `Feature::Experimental*` flag (off by default): `textmetrics`, `fontmetrics`, `is_object`, `object`, `has_key`, function-form `import`.

> `search` is registered via a multi-line `Builtins::init(` (around `builtin_functions.cc:1259`); it is the list/string search function.

## Special Variables

Dollar-prefixed names with dynamic scope. Parsed as ordinary `Identifier`s ([AST-Reference.md](AST-Reference.md) §6), but the analyzer/inliner should know they are reserved/built-in:

| Variable | Meaning |
|---|---|
| `$fn`, `$fa`, `$fs` | Tessellation resolution (facet count / angle / size) |
| `$t` | Animation time |
| `$children` | Number of children passed to the current module (`UserModuleContext` sets it) |
| `$parent_modules` | Depth of the module call stack |
| `$preview` | True in preview (F5), false in render (F6) |
| `$vpr`, `$vpt`, `$vpd`, `$vpf` | Viewport rotation / translation / distance / FOV |

> Special variables follow **dynamic** scope (a caller's `$fn` reaches into callees), unlike regular variables which are lexical. The inliner must preserve this — do **not** rename or hoist `$`-variables.

## Constants & Reserved Words

- **Constants**: `PI`. Literals `true`, `false`, `undef`.
- **Keywords** (not identifiers): `module`, `function`, `include`, `use`, `if`, `else`, `for`, `intersection_for`, `let`, `assert`, `echo`, `each`, `true`, `false`, `undef`.

## Deprecated Built-ins (cross-reference)

These parse as ordinary module/function calls and are handled per the [Spec.md](Spec.md) deprecation policy — **preserved verbatim** (info diagnostic SB5003), since rewriting them could change geometry/IO:
`import_stl`, `import_dxf`, `import_off`, `dxf_linear_extrude`, `dxf_rotate_extrude`.

Pure syntax/scope deprecations (`assign`, `child`) are **rewritten** instead — see [Diagnostics.md](Diagnostics.md) SB5001/SB5002.

---

*Source: `src/core/{Builtins,CsgOpNode,control,TransformNode,CgalAdvNode,ColorNode,OffsetNode,RenderNode,LinearExtrudeNode,RotateExtrudeNode,ProjectionNode,RoofNode,primitives,TextNode,ImportNode,SurfaceNode,GroupModule,builtin_functions}.cc`, `src/io/dxfdim.cc` @ `openscad-2019.05-3933-g6b81cb63e`. Regenerate by grepping `Builtins::init(` when targeting a different OpenSCAD version.*
