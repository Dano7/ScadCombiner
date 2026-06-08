# Slice 4 — Semantic Analysis & Symbol Table

**Status**: Implementation-ready. Self-contained with [AST-Reference.md](../AST-Reference.md) (nodes, reference-keyed side tables), [Spec.md](../Spec.md) (scoping/`use` semantics), [Builtins-Reference.md](../Builtins-Reference.md) (reserved names), [Diagnostics.md](../Diagnostics.md) (codes), and [Test-Corpus.md](../Test-Corpus.md) (`S-001`,`S-002`). Ground truth: OpenSCAD `ScopeContext.cc`, `LocalScope.cc`, `Context.cc` (`openscad-2019.05-3933`).

**Outcome**: build symbol tables and resolve every reference under OpenSCAD's scoping rules, producing the **`ISemanticModel`** that the Slice 5 inliner consumes (per-file declarations, reference→declaration binding, transitive private-constant sets), plus semantic validation diagnostics. **This slice owns `ISemanticModel`** (sketched in [Slice 5 §4](Slice-5-Loader-Inliner.md); authoritative here).

---

## 1. Exit Criteria

- [ ] `SemanticAnalyzer.Analyze(LoadGraph)` returns an `ISemanticModel` + diagnostics; never throws.
- [ ] Per-file declaration queries (`Modules`/`Functions`/`TopLevelVariables`) return exactly the top-level declarations of each file, in declaration order.
- [ ] `Resolve(reference)` binds a reference to its **top-level** `Symbol`, or returns `null` for a local binding / parameter / built-in / special variable / unresolved — per the scoping rules in §5.
- [ ] `ReferencesTo(symbol)` returns every reference bound to that declaration (for the inliner's rename rewriting).
- [ ] `PrivateConstants(usedFile)` returns the transitive set of that file's own top-level constants reachable from its exported modules/functions (§7) — the V2 enabler.
- [ ] Within-scope duplicate detection: variable reassignment → **SB3003**, module/function redefinition → **SB3004** (last-wins, per `LocalScope.cc`).
- [ ] Validation: invalid `.member` → **SB3001** (`S-001`); comprehension generator outside a vector → **SB3002** (`S-002`); conservative unknown-reference → **SB3005**.
- [ ] Special variables (`$fn`, …) are classified as dynamically-scoped and **never** resolve to a renameable symbol.
- [ ] Line coverage of `Semantics/` ≥ 95%.

---

## 2. Scope

**In:** scope-tree construction; name binding/reference resolution under OpenSCAD rules; built-in recognition; within-scope duplicate detection; the validation diagnostics; `PrivateConstants` reachability; the `ISemanticModel` surface.

**Out:** **cross-file collision *resolution*** (renaming/namespacing) and the actual flattening — those are the Slice 5 inliner (which *uses* this model's per-file queries + `ReferencesTo`). Type evaluation/constant folding (we do not evaluate expressions). The loader (Slice 5) and parser (Slices 1–3) are consumed as-is.

> Division of labor with Slice 5: Slice 4 supplies the **facts** (declarations, bindings, reachability) and flags **within-scope** duplicates; the inliner detects and resolves **cross-file** collisions using those facts and the include/use graph topology.

---

## 3. Inputs / Outputs & API

```csharp
namespace ScadBundler.Core.Semantics;

public enum SymbolKind { Module, Function, Variable }

/// Identifies a top-level declaration. Declaration is the
/// ModuleDefinition / FunctionDefinition / AssignmentStatement node.
public sealed record Symbol(SymbolKind Kind, string Name, SourceFile File, AstNode Declaration);

public interface ISemanticModel
{
    IReadOnlyList<ModuleDefinition>    Modules(SourceFile file);
    IReadOnlyList<FunctionDefinition>  Functions(SourceFile file);
    IReadOnlyList<AssignmentStatement> TopLevelVariables(SourceFile file);

    /// Top-level constants of `usedFile` transitively referenced by its exported
    /// modules/functions — the "private constants" to carry when inlining a `use`.
    IReadOnlyList<AssignmentStatement> PrivateConstants(SourceFile usedFile);

    /// Bind a reference (Identifier in value position, or a call name) to the
    /// top-level declaration it resolves to, or null for local/param/builtin/
    /// special-var/unresolved. Keyed by reference identity (ReferenceEqualityComparer).
    Symbol? Resolve(AstNode reference);

    /// All references bound to a declaration (for rename rewriting).
    IReadOnlyList<AstNode> ReferencesTo(Symbol declaration);
}

public sealed record SemanticResult(ISemanticModel Model, IReadOnlyList<Diagnostic> Diagnostics);

public sealed class SemanticAnalyzer
{
    /// Analyze a loaded graph (cross-file: follows include merges, use imports). Never throws.
    public static SemanticResult Analyze(LoadGraph graph);     // LoadGraph from ScadBundler.Core.Loading (Slice 5)
    /// Analyze a single parsed file (validation + own-scope model); for unit tests.
    public static SemanticResult Analyze(ScadFile file);
}
```

> Side tables (resolution map, references-to map) use `ReferenceEqualityComparer` (AST-Reference §15.6). The model is built over the **pre-inline** graph (the inliner needs it *before* flattening).

> **Seam types.** `LoadGraph`/`LoadedFile`/`IncludeEdge`/`UseEdge` (namespace `ScadBundler.Core.Loading`) are introduced by **this** slice as the analyzer's input contract; Slice 5's `SourceLoader` *populates* them (resolving paths, detecting cycles). An `IncludeEdge`/`UseEdge` carries the originating statement and its resolved target (`null` when unresolved; a `UseEdge` may instead be a `FontPassthrough`). `Uses` is in source order; resolution consults it last-first (last-`use`-wins).

---

## 4. The scope tree

Each construct below introduces a **scope** (a name environment). Resolution walks innermost→outermost.

| Scope | Introduced by | Binds |
|---|---|---|
| Comprehension | `for`/`let` generator inside `[ … ]` | its bindings, for the rest of that comprehension |
| Function-literal | `function (params) body` | params |
| `let` | `let(…)` (statement/expression/comprehension) | its bindings |
| `for` | `for(…)` / `intersection_for(…)` | its loop bindings |
| Function body | `FunctionDefinition` | its parameters |
| Module body | `ModuleDefinition` | its parameters + locals/nested defs in the body |
| **File (top-level)** | `ScadFile` | top-level modules, functions, variables |
| Used-library | `use`d files | their modules/functions only (lookup, not a lexical parent) |
| Builtins | [Builtins-Reference.md](../Builtins-Reference.md) | built-in modules/functions, `PI`, special vars |

Only **File-scope** declarations produce renameable `Symbol`s. Everything inner (params, `let`/`for`/comprehension bindings, function-literal params) is **local** → `Resolve` returns `null` (the inliner never renames locals).

---

## 5. Binding & resolution rules (OpenSCAD-accurate)

**Variables are not sequential** — within a scope, all assignments of a name are collected and the **last wins** for the whole scope (`parser.y`/`LocalScope.cc`). Resolution binds a read to the *scope*, not to a particular assignment; the value is that scope's last assignment.

**Variable read** `x` (Identifier in value position):
1. If `x` starts with `$` → **special variable** (dynamic scope): classify as special; `Resolve` = `null`; never rename.
2. Search scopes innermost→outermost. If found in a **local** scope (comprehension/let/for/params/module-locals) → local; `Resolve` = `null`.
3. Else if found in **file** scope → `Symbol(Variable)`. (Variables are **not** imported by `use` — never consult used-libraries for variables.)
4. Else unresolved → OpenSCAD yields `undef`; emit conservative **SB3005** (Warning) only when confident (all files loaded, not special, not local).

**Module call** `m(...)` / **function call** `f(...)` — lookup order mirrors `ScopeContext.cc`:
1. nested defs in enclosing module bodies → file-scope defs ⇒ `Symbol(Module|Function)`.
2. else **built-in** (Builtins-Reference) ⇒ `Resolve` = `null` (don't rename).
3. else **used libraries** (the file's `use`d files; **last-`use`-wins** per `SourceFile.cc` front-insertion) ⇒ `Symbol` in that used file (the inliner imports/renames it).
4. else unknown ⇒ **SB3005** (Warning), mirroring OpenSCAD's "Ignoring unknown module/function".

> Own-scope shadows built-ins (a user `module cube()` shadows the primitive). Used-libraries are consulted **after** built-ins. A user-file declaration shadows a used-library one of the same name (so the user's call binds to their own; the used one is reachable only from its own library's code).

> **Deprecated `assign`.** A `ModuleInstantiation` named `assign` is modeled as a `let`-like binding scope for its child (its named arguments bind for the child body), so the child's reads resolve correctly and don't false-positive **SB3005**. This is forward-consistent with the inliner's `assign`→`let` rewrite (SB5001); Slice 4 only models the scope, it does not rewrite the node.

**Member access** `e.x`: validate the member ∈ {x,y,z} → else **SB3001**. (Not a name-binding.)

---

## 6. Symbol-table construction

1. For each loaded file, walk its `ScadFile` once, building the scope tree (§4). Record file-scope `Modules`/`Functions`/`TopLevelVariables` in declaration order.
2. **Within-scope duplicates** (per `LocalScope.cc`): a second declaration of a name in the same scope overwrites for lookup (last-wins). Emit **SB3003** for a repeated variable assignment; **SB3004** for a repeated module/function definition. (These are *within one scope*; cross-file duplicates that arise from include-merging are the inliner's concern.)
3. Index built-in names and special variables from [Builtins-Reference.md](../Builtins-Reference.md) for recognition (treat unknown names as user/library symbols — the version-robustness rule).

## 7. `PrivateConstants(usedFile)` — transitive reachability

Computes the top-level constants a `use`d file's exported callables need (so the inliner can carry them as private constants — the V2 behavior):

1. Seed = the file's exported `Modules` + `Functions`.
2. Walk each seed's body; for every variable read that `Resolve`s to one of **this file's own** top-level variables, add that variable's `AssignmentStatement` to the result set.
3. Transitively close: also walk the bodies of added constants' initializers (a constant may reference other top-level constants), and any module/function those reference.
4. Return the collected assignments (deduped, in declaration order). Geometry, unreferenced variables, and `$`-var settings are **excluded**.

> This is what lets the inliner emit a `use`d library's `WALL = 2;` alongside `module box() cube(WALL);` while dropping its top-level geometry — and namespace them on collision so the using file cannot perturb them.

## 8. Validation diagnostics (this slice)

| Code | Sev | Trigger |
|---|---|---|
| SB3001 | Error | `MemberExpression` with member ∉ {x,y,z} (`S-001`) |
| SB3002 | Error | comprehension generator (`for`/`forc`/`if`/`let`/`each`) outside a `VectorExpression` (`S-002`; defensive — the parser already restricts position) |
| SB3003 | Warning | variable reassigned in a scope (last-wins) |
| SB3004 | Warning | module/function redefined in a scope (last-wins) |
| SB3005 | Warning | **(new)** conservative unknown reference — see below |

**SB3005 — Unknown reference** *(Warning, Semantic)*: emitted only when confident — all files loaded, the name is not a built-in, special variable, local binding, or any reachable user declaration. Message: `Unknown {module|function|variable} '{name}'.` Mirrors OpenSCAD's "Ignoring unknown …" warnings; conservative by default to avoid false positives from library names the analyzer can't see. Add to [Diagnostics.md](../Diagnostics.md).

## 9. Test plan

- **Validation**: `S-001` invalid `.w` → SB3001 (+ positive `.x` no-diag); `S-002` `each` outside vector → SB3002 (+ positive `[each …]`).
- **Duplicates**: variable assigned twice → SB3003; module defined twice → SB3004 (OpenSCAD `moduleoverload` fixture).
- **Resolution**: a variable shadowed by a `let`/`for`/param binding → `Resolve` = null (local); a top-level variable read → `Symbol(Variable)`; a `$fn` read → special (null); a call to a built-in (`cube`) → null; a call to a user module → `Symbol(Module)`; a call resolving into a `use`d library → `Symbol` in that file; last-`use`-wins across two used libs.
- **ReferencesTo**: all and only the references bound to a declaration are returned (drives rename tests in Slice 5).
- **PrivateConstants**: `B-002`'s `lib.scad` → `{WALL}` (referenced by `box`), excluding `$fn`, `UNUSED`, and geometry; transitive case (constant referencing another constant).
- **Unknown**: call to a truly-undefined module → SB3005; a library name the model *can* see → no SB3005.
- **No-throw**: malformed/partial ASTs (from parser recovery) analyze without throwing.
- Cross-file fixtures: OpenSCAD `tests/data/modulecache-tests/{multipleA,multipleB,multiplecommon,moduleoverload,use,used}`.

## 10. Worked example (`PrivateConstants`)

`lib.scad`:
```scad
$fn = 64;
WALL = 2;
GAP = WALL / 2;        // constant referencing another constant
UNUSED = 5;
module box() cube([WALL, WALL, GAP]);
cube(99);              // top-level geometry
```
`PrivateConstants(lib)` = `{ WALL, GAP }` (reachable from `box`; `GAP` pulled in transitively). Excluded: `$fn` (special), `UNUSED` (unreferenced), `cube(99)` (geometry). This is exactly the set the Slice 5 inliner carries (namespaced on collision) when another file does `use <lib.scad>`.

## 11. Definition of Done

All §1 boxes checked; `S-001`/`S-002` pass; resolution/duplicate/`PrivateConstants` suites pass (incl. the OpenSCAD `modulecache-tests` fixtures); `ISemanticModel` satisfies the Slice 5 §4 contract; never throws; `Semantics/` coverage ≥95%. Slice 5's inliner then consumes this model to flatten, collide-resolve, and import.
