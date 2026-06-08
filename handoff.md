# Handoff — Start Here (Slice 5: Source Loader & Inliner)

You are picking up **ScadBundler**, an AST-based OpenSCAD file bundler (C# / .NET 10, distributed as a `dotnet tool`). **Slices 1 (lexer), 2 (AST + parser), 3 (comprehensions + functional exprs), and 4 (semantic analysis) are complete and committed.** Your job this session is **Slice 5 — the `SourceLoader` + `Inliner`**: recursive `include`/`use` resolution (populating the `Loading/` graph), cross-file collision **resolution** (rename/namespace), flattening, deduplication, and deprecated-construct normalization — producing one bundled `ScadFile`. This is the bundler's core.

---

## 🔴 DO THIS FIRST: cold code review of Slice 4

**Before writing any Slice 5 code, do a fresh, critical review of the Slice 4 implementation.** You did not write it this session — read it as a reviewer would. Slice 5 consumes the `ISemanticModel` and the `Loading/` seam directly, so any latent semantic bug surfaces here. Review goals:

- **Resolution rules.** Read `src/ScadBundler.Core/Semantics/SemanticAnalyzer.cs`: the two-pass design (`BuildFileScope` duplicate detection → `ResolveFile` walk), the `Resolve*` rules (variable read; module/function call: own → built-ins → used libraries, last-`use`-wins; `IsLocal` scope chain), `LookupOwnOrIncluded` (own + `include`-merge), the `FileEnvironment`/`IsComplete` SB3005 gate, `CollectBodyLocals` (module-body local hoisting), and the `assign` binding-scope special case. Cross-check against OpenSCAD `C:\git\hub\openscad\src\core\` — `ScopeContext.cc` (`FileContext::lookup_local_*` = own → builtins → usedlibs), `LocalScope.cc` (last-wins), `SourceFile.cc` `registerUse` (front-insertion ⇒ last-`use`-wins), `parser.y` `handle_assignment` (SB3003 positioning).
- **The model surface.** `ISemanticModel` (`Modules`/`Functions`/`TopLevelVariables`/`PrivateConstants`/`Resolve`/`ReferencesTo`) and `SemanticModel`'s `PrivateConstants` reachability closure. **This is your Slice-5 input** — confirm it gives you what the inliner needs (per-file decls, reference→symbol binding, references-to for rename, private-constant sets).
- **The seam.** `src/ScadBundler.Core/Loading/LoadGraph.cs` (`LoadGraph`/`LoadedFile`/`IncludeEdge`/`UseEdge`) — created in Slice 4 as the analyzer's input. **Slice 5 builds the loader that populates these.**
- **Tests.** `SemanticValidationTests`, `SemanticResolutionTests`, `SemanticCrossFileTests`, `SemanticModelTests`, `SemanticWalkTests`, `Slice4CorpusTests`, and `tests/.../TestSupport/SemanticHelper.cs` (graph builder + AST search you can reuse).

Optional: run `/code-review` on the last commit. Record anything non-trivial, then proceed.

---

## Current state

- **Slices 1–4 done:** `dotnet build` zero-warning (warnings-as-errors), `dotnet test` green (**317 tests**). Coverage: `Lexing/` ≈98%, `Parsing/` ≈99%, **`Semantics/` 100%, `Loading/` 100%**.
- Branch is **`Claude_implementation`**. Last feature commit: `feat(semantics): implement Slice 4 …`.
- `src/ScadBundler.Core/`: `Text/`, `Trivia/`, `Diagnostics/`, `Lexing/`, `Ast/`, `Parsing/`, **`Semantics/`** (analyzer + model), **`Loading/`** (seam types only — `LoadGraph` et al.; **no loader yet**). `Inlining/`, `Emitting/` do not exist yet.
- **Diagnostics:** SB3001–SB3005 are catalogued in `docs/Diagnostics.md` **and** added to `DiagnosticCode.cs`. Loader SB4001/SB4002 and normalization SB5001–SB5003 are catalogued; **SB5004/SB5005 are catalogued in the doc but NOT yet in `DiagnosticCode.cs`** — add them there before use. The B-006 collision code is **SB5004** (rename/namespace).

## What to read, in order

1. **`docs/slices/Slice-5-Loader-Inliner.md`** — your primary spec (the `SourceLoader` algorithm §5, the six-phase `Inliner` §6, the public API §7, `B-001`..`B-007`). The §4 "consumed contract" is already satisfied by Slice 4's `ISemanticModel`.
2. **`docs/Spec.md`** — `include`/`use` semantics, **File Resolution** order (search paths + cycles), **last-wins** collision rules, the `use`-of-fonts pass-through, and the deprecation policy. All source-verified.
3. **`docs/AST-Reference.md`** — node shapes; **§15.6** the reference-keyed side-table pattern (transforms build new immutable nodes via `with`; resolution facts live in `Dictionary<AstNode,T>(ReferenceEqualityComparer.Instance)`).
4. **`docs/Diagnostics.md`** — the SB4xxx/SB5xxx catalog. **Add SB5004/SB5005 to `DiagnosticCode.cs`** before using them; never invent codes ad hoc.
5. **`docs/Builtins-Reference.md`** — reserved names (so the inliner doesn't rename built-ins).
6. Ground truth: OpenSCAD `C:\git\hub\openscad\src\core\` — `parsersettings.cc` (`find_valid_path` search order), `SourceFile.cc` (`registerUse`/`usedlibs` front-insertion), `ScopeContext.cc`/`LocalScope.cc`. Fixtures: `tests/data/modulecache-tests/{circular*,multiple*,moduleoverload,use,used,main-use-include,simpleinclude}`.

## Slice 5 seam (semantics → loader/inliner)

- **`SourceLoader.Load(rootPath, options)` → `LoadGraph`** — the `Loading/` records already exist; populate them. Resolve each `include`/`use` raw path per Spec "File Resolution"; track the active path stack for cycle detection (**SB4002**); font `use` (`.ttf`/`.otf`) → `UseEdge { FontPassthrough = true }`; not-found → **SB4001**, `Target = null`. Cache by absolute path (load once; diamonds are DAGs). **Never throw.**
- **`SemanticAnalyzer.Analyze(graph)` → `ISemanticModel`** — already implemented; call it between load and inline (`SourceLoader → Lexer/Parser → SemanticAnalyzer → Inliner`).
- **`Inliner` consumes the model** — use `Resolve`/`ReferencesTo` for collision detection + rename rewriting (rewrite **only** the references bound to a renamed symbol — never a same-named symbol that binds elsewhere), `PrivateConstants(usedFile)` for `use`-import (carry these constants, namespace on collision), per-file decl queries for the merged set. The model is built over the **pre-inline** graph — keep it; don't re-analyze after flattening.
- **Output** is `BundleResult(ScadFile Bundled, IReadOnlyList<Diagnostic>)` — loader + semantic + inliner diagnostics, source-ordered. Slice 5 emits **AST, not text** (the emitter is Slice 6).

## Conventions carried over (so the build stays green)

- **Strict build:** `Directory.Build.props` sets net10.0, nullable, `TreatWarningsAsErrors`, analyzers `latest-Recommended`, `GenerateDocumentationFile`. Every **public** Core member needs XML docs (CS1591 = error). Watch **CA1859** (private/internal helpers that always return one concrete type must declare it) and **CA1822** (mark helpers `static` when they don't touch instance state) — both bit Slice 4 mid-build.
- **Immutable AST + reference-keyed side tables:** transforms produce new nodes via `with`; synthetic/renamed nodes reuse their **origin node's `SourceSpan`** (AST-Reference §2), or `SourceSpan.Synthetic` when there's no origin. Side tables use `ReferenceEqualityComparer.Instance` (note: `IEqualityComparer<in T>` is contravariant, so `new Dictionary<AstNode,T>(ReferenceEqualityComparer.Instance)` compiles — but `Enumerable.ToDictionary(…, comparer)` infers `TKey=object?` and fails; build those dictionaries with an explicit loop).
- **Golden corpus:** add `tests/Corpus/slice5-bundle/<id>/` cases (`main.scad` + libs + `options.txt` + assertions). Per Test-Corpus §4, bundle **assertions are binding** (presence/absence/rewrite/normalization); exact output text becomes a golden only once Slice 6 locks emitter formatting. Reuse `SemanticHelper.Graph(...)` for in-memory multi-file fixtures (don't depend on the external OpenSCAD checkout — vendor equivalent inline fixtures, as Slice 4 did).
- **Diagnostics:** add SB5004/SB5005 to `DiagnosticCode.cs` (with XML docs) before using; messages must match `docs/Diagnostics.md` exactly (the corpus asserts message text).

## Non-negotiables (Constitution)

Hand-written passes — **no parser generators / ANTLR / regex** in the core path. Immutable AST records. **Collect diagnostics, never throw** (loader and inliner both). **≥95% line coverage** of `Loading/` + `Inlining/`. No runtime interop with OpenSCAD's C++ (reference/fixtures only). Output must be **semantically equivalent** to input (e.g. diamond-include geometry is intentionally duplicated; `include` duplicates are last-wins; `use` libraries are namespaced on collision to preserve isolation — the V2 guarantee).

## Commands

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~SemanticAnalyzerTests"   # or ~Inliner once it exists
dotnet test --collect:"XPlat Code Coverage"
```

## Workflow / repo conventions

- Commits on `Claude_implementation`, **conventional commits**, ending with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer. Commit when a unit is done; don't push unless asked.
- `.gitattributes` forces **LF** everywhere.
- If you find a genuine spec gap/ambiguity, **fix the spec too** — this project's whole point is one-shot, spec-driven implementation. (Slice 4 did this for the Test-Corpus `S-002` case: the parser already rejects a bare generator outside `[ … ]`, so the clean-parsing SB3002 case uses the range-start form `[gen : end]`; see `docs/Test-Corpus.md` §5.)

## Watch items inherited from Slice 4

- **`PrivateConstants(usedFile)`** returns that file's own top-level constants reachable from its exported modules/functions (deduped, declaration order), excluding geometry/`$`-vars/unreferenced vars. This is exactly the set to carry on `use`-import (namespace on collision).
- **Last-`use`-wins**: `LoadedFile.Uses` is in **source order**; the analyzer consults it last-first. When your loader builds `Uses`, keep source order (the analyzer handles the reversal).
- **`assign`** is modeled as a `let`-like binding *scope* in Slice 4 resolution, but the *node rewrite* `assign`→`let` (SB5001) is **yours** (Phase E). Likewise `child`→`children` (SB5002).
- **SB3002** is defensive — the parser never emits a generator outside `[ … ]`. When the inliner synthesizes/rewrites ASTs, keep comprehension generators in valid vector positions so re-analysis (if any) stays clean.

## After Slice 5

Slice 6 (`docs/slices/Slice-6-Emitter-CLI.md`) adds the `Emitter` (pretty-printer; preserves Customizer/license trivia; the SB6001 round-trip self-check) and the `src/ScadBundler` CLI (`bundle` command, `dotnet tool`). Pipeline: `SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → Emitter`.
