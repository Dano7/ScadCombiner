# Slice 5 — Source Loader & Inliner

**Status**: Implementation-ready. This is the bundler's core. Self-contained with [Spec.md](../Spec.md) (the `include`/`use`, resolution, and collision **semantics** — all source-verified), [AST-Reference.md](../AST-Reference.md) (nodes + reference-keyed side tables), [Diagnostics.md](../Diagnostics.md) (codes), [Builtins-Reference.md](../Builtins-Reference.md) (reserved names), and [Test-Corpus.md](../Test-Corpus.md) (`B-001`..`B-007`). Ground truth: OpenSCAD `parsersettings.cc`, `SourceFile.cc`, `ScopeContext.cc`, `LocalScope.cc` (`openscad-2019.05-3933`).

**Outcome**: given a root `.scad` path, produce one **flattened `ScadFile` AST** that renders identically in OpenSCAD — with `include`/`use` resolved, deprecated constructs normalized, collisions resolved, and duplicate definitions deduplicated. The Emitter (Slice 6) renders this AST to text; Slice 5 outputs **AST, not text**.

---

## 1. Exit Criteria

- [ ] `Bundler.Bundle(rootPath, options)` returns a single `ScadFile` + diagnostics; never throws on malformed/missing input.
- [ ] **Resolution** follows Spec "File Resolution" order (file dir → `OPENSCADPATH`/`-p` → user libs → built-in libs); absolute paths used directly; missing → SB4001.
- [ ] **Cycle detection**: an `include`/`use` ancestry cycle is reported (SB4002) and broken — no infinite loop. (Fixtures: OpenSCAD `tests/data/modulecache-tests/circular*`.)
- [ ] **`include`** is fully inlined (defs + vars + geometry, recursively, in document order); duplicate definitions resolve **last-wins** by default (SB3004); duplicated top-level **geometry** from a diamond include is **preserved** (semantic equivalence).
- [ ] **`use`** imports only module/function definitions + their transitively-referenced **private constants**; drops geometry, unreferenced vars, and top-level `$`-var settings; isolates them by namespacing **on collision**, rewriting internal references (resolves V2 behavior). Font `use <…ttf/otf>` passes through verbatim.
- [ ] **Collision strategy** is origin-dependent by default (`include`→keep-last, `use`→prefix) and overridable via `--on-collision`; renames rewrite all bound references (SB5004).
- [ ] **Deduplication** merges structurally-identical definitions arriving via multiple paths (SB5005), ignoring spans/trivia.
- [ ] **Normalization**: `assign`→`let` (SB5001), `child()`→`children(0)`/`child(n)`→`children(n)` (SB5002); deprecated built-ins preserved + flagged (SB5003).
- [ ] Customizer comment trivia and (with `--bundle-licenses`) license headers are preserved/aggregated.
- [ ] Test-Corpus `B-001`..`B-007` pass; line coverage of `Loading/` + `Inlining/` ≥ 95%.

---

## 2. Scope

**In:** the `SourceLoader` (resolve → load → parse → graph → cycle detection) and the `Inliner` (flatten include/use → resolve collisions → dedup → normalize → assemble one `ScadFile`).

**Out:** the lexer/parser ([Slices 1–3], used as-is); the **semantic analysis itself** ([Slice 4] — symbol tables, scope/reference resolution, collision *detection*), which Slice 5 **consumes** via the contract in §4; the emitter and CLI ([Slice 6]). `--minify` is an emitter concern.

---

## 3. Pipeline & data flow

```
rootPath
  └─ SourceLoader.Load ─────────► LoadGraph (root + loaded files, include/use edges)   [§5]
        each file: Lexer+Parser (Slices 1–3)
  └─ SemanticAnalyzer.Analyze ──► ISemanticModel  (Slice 4 — consumed, §4)
  └─ Inliner.Bundle ───────────► BundleResult { ScadFile Bundled, Diagnostics }         [§6]
        phases: inline-includes → import-uses → resolve-collisions → dedup → normalize → assemble
```

---

## 4. Consumed contract from Slice 4 (the Slice 4 ↔ 5 interface)

Slice 5 needs reference/symbol facts it must not recompute. **[Slice 4](Slice-4-Semantic.md) owns and finalizes `ISemanticModel` + `Symbol`** (authoritative there). The Inliner depends only on these queries:

```csharp
public interface ISemanticModel
{
    // Top-level declarations of a loaded file (in declaration order).
    IReadOnlyList<ModuleDefinition>      Modules(SourceFile file);
    IReadOnlyList<FunctionDefinition>    Functions(SourceFile file);
    IReadOnlyList<AssignmentStatement>   TopLevelVariables(SourceFile file);

    /// For a USED file: the top-level constant assignments that its exported
    /// modules/functions transitively reference (the "private constants" to carry).
    IReadOnlyList<AssignmentStatement>   PrivateConstants(SourceFile usedFile);

    /// Resolve a reference node (Identifier used as a value, or a call's name)
    /// to the declaration it binds to under OpenSCAD scoping, or null if unresolved
    /// (built-in or unknown). Keyed by reference identity (ReferenceEqualityComparer).
    Symbol? Resolve(AstNode reference);

    /// All references that bind to a given declaration (for rename rewriting).
    IReadOnlyList<AstNode> ReferencesTo(Symbol declaration);
}
```

> `Symbol` identifies a declaration (file + name + kind: Module | Function | Variable). If Slice 4 is not yet implemented when Slice 5 is built, provide a minimal in-slice implementation covering top-level declarations and reference resolution under OpenSCAD scoping (current-file shadows `use`d; `use` lookup is last-`use`-wins per `SourceFile.cc`).

---

## 5. SourceLoader

**Input**: root path + `BundleOptions`. **Output**: `LoadGraph`.

```csharp
public sealed record LoadedFile(SourceFile Source, ScadFile Ast,
    IReadOnlyList<IncludeEdge> Includes, IReadOnlyList<UseEdge> Uses);
public sealed record LoadGraph(LoadedFile Root, IReadOnlyDictionary<string, LoadedFile> ByAbsolutePath,
    IReadOnlyList<Diagnostic> Diagnostics);
```

Algorithm:
1. Resolve the root to an absolute path; lex+parse it (Slices 1–3). Cache by absolute path (a `SourceFileCache` analog) so a file shared by many paths is parsed once.
2. Walk the parsed AST for `IncludeStatement`/`UseStatement`. For each, **resolve** the raw path per Spec "File Resolution":
   - relative → try (a) the *including file's* directory, then (b) each `OPENSCADPATH`/`-p` entry in order, then (c) user lib dir, then (d) built-in lib dir; first existing non-directory wins.
   - absolute → use directly.
   - **font** extension (`.ttf`/`.otf`) on a `use` → mark the edge `FontPassthrough`; do **not** load/parse.
   - not found → **SB4001**, mark edge unresolved (the statement is dropped at assembly).
3. Recurse into resolved targets. Track the **active path stack**; if a resolution targets a file already on the stack → **SB4002** (cycle) and do not recurse (break the cycle). A file reached again *not* on the stack (diamond/DAG) is loaded from cache and linked (allowed).
4. Return the graph. (Loader never throws; all failures are diagnostics.)

> `include` vs `use` edges are tracked separately — the Inliner treats them differently.

---

## 6. Inliner — the flattening algorithm

Produces the bundled `ScadFile`. Transforms build new immutable nodes (`with`); resolution facts live in reference-keyed side tables (AST-Reference §15.6). Synthetic nodes reuse origin spans (§2 sentinels).

### Phase A — Inline `include` (recursive, document order)
Starting at the root's statement list, replace each **resolved** `IncludeStatement` with the **entire top-level statement list** of the included file (defs + variable assignments + geometry), recursively, **preserving document order**. Unresolved includes are removed (already flagged SB4001). A `use` statement encountered *inside* an included file is hoisted into the bundle's use-set (handled in Phase B), not spliced.

Result: one flat statement list (root + all transitively-included content) in document order. Duplicate definitions/variables are left in place here; resolution happens in Phase C.

> **Diamond includes**: a file included via two branches contributes its statements twice. Duplicate *definitions* are handled by dedup (Phase D); duplicate top-level **geometry is intentionally kept** — OpenSCAD would render it twice, so preserving it is semantic equivalence (emit an Info if desired).

### Phase B — Import `use`d definitions
For each `use`d file actually needed (a definition referenced from the bundle, transitively):
- Take its **module/function definitions** and their **`PrivateConstants`** (from `ISemanticModel`).
- These will be emitted into the bundle (hoisted — defs are order-independent). Top-level **geometry, unreferenced variables, and `$`-var settings are dropped**.
- Font-passthrough `use` edges are kept as `UseStatement`s in the output (cannot inline a binary font).
- Transitivity: if a used definition references a definition from a file *it* used, include that too (repeat until closed).

### Phase C — Resolve collisions
Detect name clashes across the merged definition/variable set (via `ISemanticModel`). Apply the strategy (`BundleOptions.OnCollision`; default `Auto`):
- **`Auto`** (origin-dependent, the correctness-preserving default):
  - `include`-origin duplicate definitions → **keep-last** (drop earlier; **SB3004**). Matches OpenSCAD's flat-scope last-wins.
  - `use`-imported names that collide with anything in the merged set → **prefix/namespace** the used symbol and its private constants, then **rewrite every reference that binds to it** (`ReferencesTo`) → **SB5004**. This preserves library isolation (a used function keeps seeing its own constant; the user's same-named symbol is unaffected — the V2 guarantee).
- **`Prefix`** / `KeepFirst` / `KeepLast`: force that strategy everywhere. **`Error`**: any collision → diagnostic, no output.
- **Namespacing scheme**: `{libstem}__{name}` (e.g. `gear_a__gear`); on secondary clash, append `_2`, `_3`, …. Renames must rewrite **all** bound references (call names, function calls, variable reads) — never rewrite a same-named symbol that binds elsewhere.

### Phase D — Deduplicate
Merge **structurally-identical** definitions (same kind, name, parameters, and body) that arrived via multiple paths (diamond include/use). Equality **ignores `Span`, trivia, and `BlankLineBefore`** (content/signature hash per [Design.md](../Design.md)). Keep one; drop the rest; **SB5005**. (Conflicting same-name/different-body defs are *not* deduped — they went through Phase C.)

### Phase E — Normalize deprecated constructs
Walk the tree (rewriting visitor):
- `ModuleInstantiation` named `assign` with named args → `LetStatement` (args→`Binding`s), **SB5001**.
- `ModuleInstantiation` named `child` → `children`: `child()`→`children(0)`, `child(n)`→`children(n)`, **SB5002**.
- deprecated built-ins (`import_stl`/`import_dxf`/`import_off`/`dxf_linear_extrude`/`dxf_rotate_extrude`) → leave unchanged, **SB5003** (Info).

### Phase F — Assemble
Build the output `ScadFile`:
1. Optional aggregated **license header** (with `--bundle-licenses`): collect leading license-comment trivia from each source file, dedup identical blocks, emit once at top.
2. **Use-imported defs + private constants** (Phase B/C output), hoisted near the top.
3. The **include-flattened root statements** in document order (Phase A), with references rewritten (Phase C) and constructs normalized (Phase E).
4. Preserve Customizer trivia on the root's top-level assignments (they are the Customizer parameters). `BlankLineBefore` retained for section separation.

Output: `BundleResult(ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics)` (loader + semantic + inliner diagnostics, source-ordered).

---

## 7. Public API

```csharp
public enum CollisionStrategy { Auto, Prefix, Error, KeepFirst, KeepLast }

public sealed record BundleOptions(
    IReadOnlyList<string> LibraryPaths,        // -p entries; OPENSCADPATH is appended
    CollisionStrategy OnCollision = CollisionStrategy.Auto,
    bool BundleLicenses = false,
    bool PreserveComments = true);

public sealed record BundleResult(ScadFile Bundled, IReadOnlyList<Diagnostic> Diagnostics);

public static class Bundler
{
    /// Full pipeline: load → analyze → inline. Never throws; failures are diagnostics.
    public static BundleResult Bundle(string rootPath, BundleOptions options);
}
```

---

## 8. Diagnostics (this slice)

Loader `SB4001`/`SB4002` and normalization `SB5001`–`SB5003` are already cataloged. **New** (add to [Diagnostics.md](../Diagnostics.md), SB5xxx — Inliner):

| Code | Sev | Trigger | Message |
|---|---|---|---|
| SB5004 | Warning | a name renamed/namespaced to resolve a collision | `'{name}' from '{file}' renamed to '{newname}' to resolve a collision.` |
| SB5005 | Info | structurally-identical definition deduplicated | `Duplicate definition '{name}' merged ({n} copies).` |

`SB3003`/`SB3004` (reassignment/redefinition last-wins) are emitted here when merging surfaces them.

---

## 9. Test plan

- **Decision-proving** (Test-Corpus): `B-001` include full-inline + geometry; `B-002` use defs-only + private constants (V2); `B-003` assign→let (SB5001); `B-004` child→children (SB5002); `B-005` deprecated built-in preserved (SB5003); `B-006` use-collision namespacing (SB5004); `B-007` include last-wins (SB3004).
- **Resolution**: relative vs `-p`/`OPENSCADPATH` vs absolute; missing → SB4001; font `use` passthrough.
- **Cycles**: direct + indirect include cycle → SB4002, terminates; diamond include (DAG) → no error, dedup applies. Use OpenSCAD `modulecache-tests/circular*`, `multipleA/B/common`, `moduleoverload`, `main-use-include` as fixtures.
- **Dedup**: same library included via two paths → defs emitted once (SB5005); but diamond-included **geometry** appears twice (preserved).
- **Collision strategies**: `Auto` (origin-dependent), `Prefix`, `Error`, `KeepFirst`, `KeepLast` each on the B-006/B-007 inputs; verify reference rewriting binds correctly.
- **Reference rewriting**: a renamed used module's internal calls and the user's call sites bind to the correct (distinct) definitions.
- **Assembly**: `--bundle-licenses` aggregates+dedups headers; Customizer trivia on root params preserved.
- **No-throw**: missing files, cycles, parse errors in dependencies → diagnostics, partial bundle, never throws.

> Assertions are binding (presence/absence/rewrite/normalization). Exact output text becomes a golden once Slice 6 locks emitter formatting (Test-Corpus §4).

---

## 10. Worked example (`use` collision → namespacing)

`gear_a.scad`: `module gear() cube(1);` · `gear_b.scad`: `module gear() sphere(1);`
`main.scad`:
```scad
use <gear_a.scad>
use <gear_b.scad>
gear();
```
Bundled `ScadFile` (illustrative; `Auto` strategy, last-`use`-wins binding):
```scad
module gear_a__gear() cube(1);
module gear_b__gear() sphere(1);
gear_b__gear();
```
`gear()` binds to the last-`use`d library (`gear_b`, per `SourceFile.cc` usedlibs front-insertion); both defs are namespaced (SB5004). See `B-006`.

---

## 11. Definition of Done

All §1 boxes checked; `B-001`..`B-007` pass; loader/inliner never throw; cycle, diamond, collision, dedup, and normalization behaviors verified (incl. against the OpenSCAD `modulecache-tests` fixtures); coverage ≥95%. Output is a single `ScadFile` the Emitter (Slice 6) can render. Where Slice 5 needed Slice 4 facts, they came through `ISemanticModel` (§4) — that contract is the spec Slice 4 must satisfy.
