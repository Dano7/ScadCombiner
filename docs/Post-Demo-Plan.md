# Post-Demo Plan — Customizer Parameters, Search-Path Fidelity, Namespacing

Requirements gaps surfaced by the **first power-user demo** (bundling
`C:\git\dan\SCAD\ForkedHolder.scad`). The pipeline is complete and green; these are *correctness and
fidelity* gaps in the bundled output, not new pipeline stages. This doc analyzes each, pins it to
OpenSCAD ground truth, and gives a concrete plan with a recommended disposition.

> Companion to [Post-v1-Plan.md](Post-v1-Plan.md) (license aggregation, cross-`include` mis-bind,
> block-scope dup detection). The items here are independent of those.

## Summary

| # | Item | Type | Effort | Risk | Disposition |
|---|------|------|--------|------|-------------|
| A | **Main-file Customizer parameters preserved** | Correctness bug | M | Med | ✅ **Done** (this session) |
| B | OpenSCAD-faithful search paths + `OPENSCADPATH` | Fidelity gap | S–M | Low | ✅ **Done** (this session) |
| C | **Always-namespace `use` imports** (default) | Correctness/fidelity | M | Med | ✅ **Done** (this session) — see [ADR 0001](adr/0001-include-use-scoping-and-namespacing.md) |
| D | Obfuscator (`id_xxxxxx`) | Feature (opt-in) | M | Low | **Defer to vNext** |

Items C and D both reuse the rename machinery and the **main-parameter exemption** that Item A
introduces, so **A is a strict prerequisite** for both. The demo's "prefix *every* identifier" idea is
**rejected** for `include` and `$`-vars (it would diverge from OpenSCAD) — see [ADR 0001](adr/0001-include-use-scoping-and-namespacing.md);
the decided change (C) is the narrower "always-namespace `use`". Sequence: **A → B → [#4 mis-bind] → C →
(vNext) D.**

> **Status (this session):** A and B are implemented, tested, and verified end-to-end against the real
> `C:\git\dan\SCAD\ForkedHolder.scad` (its `wall_thickness … tine_count` now lead the bundle, fenced
> from the `goews_*` library globals by `/* [Hidden] */`). See the per-item "Implemented" notes below.
> 538 tests green, zero warnings. C and D remain as scoped here.

---

## A. Main-file Customizer parameters are lost in the bundle — ESSENTIAL v1

### Symptom (from the demo)

Opening the **source** `ForkedHolder.scad` in OpenSCAD, the Customizer offers the model's real knobs:
`wall_thickness, tine_base_height, tine_inside_length, tine_lip_height, inter_tine_gap, upward_angle,
tine_count`. Opening the **bundled** `ForkedHolder-combined.scad`, the Customizer instead offers only
`goews_fundamental_unit` and `goews_base_thickness` — two constants from a transitively-included
library. The user's actual parameters have vanished from the Customizer.

### Root cause (OpenSCAD ground truth)

OpenSCAD's Customizer extracts parameters in
[`CommentParser::collectParameters`](file:///C:/git/hub/openscad/src/core/customizer/CommentParser.cc).
A top-level assignment becomes a Customizer parameter **iff all three hold**:

1. **It is a literal** — `assignment->getExpr()->isLiteral()` (number/bool/string/simple vector). A
   computed RHS is never a parameter.
2. **It is before the first `{`** — `getLineToStop()` scans the file text and returns the line of the
   **first top-level `{`** (brace outside a string/comment). Assignments on or after that line are
   skipped. (Grouping is also bounded here.)
3. **It physically belongs to the root file** — the filter explicitly drops assignments whose
   `location.fileName()` differs from the root file. *Included* files' assignments are never
   Customizer parameters.

Groups come from the nearest preceding `/* [Group] */` block comment; the group named **`Hidden`** is
filtered out of the GUI (its members are not shown).

Now overlay the bundle. `ForkedHolder.scad` begins with `include <forkedholderlib.scad>`, which
(transitively) `include`s `goews.scad`. The Inliner flattens includes in document order
([Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs) `Assemble`), so the **entire library lands
above the root's own assignments**, and any `use`-imports are hoisted higher still. The bundle starts:

```scad
goews_fundamental_unit = 42;                    // literal, before first brace → shown
goews_staggered_x_spacing = goews_fundamental_unit / 2;  // computed → not a parameter
...
goews_base_thickness = 3;                        // literal, before first brace → shown
module goews_cleat() { ... }                     // ← first '{' in the file; getLineToStop stops here
...
wall_thickness = 6;                              // now AFTER the first brace → skipped
```

So OpenSCAD's three rules now select exactly `goews_fundamental_unit` and `goews_base_thickness` —
matching the demo screenshot — and reject the user's real parameters because they sit past the first
`{`. The bug is **statement ordering**, introduced by inlining.

Note the user's source already uses the convention deliberately: `ForkedHolder.scad` hand-writes
` /* [Hidden] */` before `cleat_stagger=true;`, and its `cleat_spacing_x = goews_staggered_x_spacing`
lines are intentionally computed so they don't appear as knobs. The bundler must *preserve* this
authoring, not scramble it.

### Design — hoist the root's parameter prologue, fence the rest with `/* [Hidden] */`

Re-order the bundle so the root file's parameter block leads and everything else is fenced off:

```
<root parameter prologue, verbatim, in original order, NOT renamed>
/* [Hidden] */                ← synthesized boundary
<fonts> <use-imports> <flattened library + geometry — the current body>
```

This satisfies all three OpenSCAD rules: the root's literal params are now before any `{` **and**
before the `Hidden` fence (so they show, with their original `/* [Group] */` headers intact); every
other top-level literal is either past the fence (→ `Hidden` group) or past the first `{` (→ skipped).
It mirrors exactly what the user already does by hand.

**Defining the "parameter prologue"** (structural approximation of rules 2–3, on the *root* AST —
`graph.Root.Ast.Statements`, pre-flatten):

> The maximal leading run of root-file top-level **`AssignmentStatement`s**, skipping over any leading
> `include`/`use`/`EmptyStatement`, and stopping at the first statement that opens a brace or emits
> geometry — `ModuleDefinition`, `FunctionDefinition`, `ModuleInstantiation`, `IfStatement`,
> `ForStatement`, `IntersectionForStatement`, `LetStatement`, or `BlockStatement`.

The prologue is captured by node identity, so the same nodes are skipped when the body is emitted.
It includes the interleaved *computed* assignments (e.g. `cleat_spacing_x = …`) — harmless, because
OpenSCAD excludes them by the literal rule — and preserves the author's own `/* [Hidden] */` /
`/* [Group] */` trivia in place. Hoisting computed assignments above their dependencies is safe:
OpenSCAD binds all top-level assignments in one unified scope (last-wins, full forward visibility), so
relative order of assignments never changes the result.

**The `/* [Hidden] */` boundary** is synthesized as `CommentTrivia("/* [Hidden] */", Block)` (span
`SourceSpan.Synthetic`) prepended to the `LeadingTrivia` of the **first post-prologue statement**, with
`BlankLineBefore = true`. This reuses the emitter's existing trivia path (no emitter change) and
**round-trips** under the SB6001 self-check, because a comment re-parses to a comment (trivia is
ignored by `StructuralKey`). Emit the fence **whenever there is any body** — even when the prologue is
empty — so a root that declared zero parameters keeps showing zero (no library constant leaks in).

**Main-parameter exemption.** Prologue assignments must never be renamed, namespaced, or obfuscated
(the end user needs to read them). They are pinned winners with their original names; a collision
between a prologue name and any other definition is resolved by acting on the **other** side
(namespace the `use`-import; drop the include-duplicate with the usual SB3003/SB3004), never the
prologue node. This exemption is the hook Items C and D depend on.

### Implementation steps

1. **[Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs)**
   - Add `ExtractParameterPrologue(root)` → ordered `List<AssignmentStatement>` + a
     `HashSet<AstNode>` (identity) per the rule above.
   - In `RootDefinitions`/`ResolveCollisions`, mark prologue nodes **protected**: keep them out of
     rename/drop, force them as winners with original names (resolve collisions on the other side).
   - In `Assemble`, emit order becomes: **prologue (rewritten for normalization only)** → fonts →
     use-imports → body (`rootFlat` minus prologue nodes). Prepend the synthesized `/* [Hidden] */`
     trivia to the first post-prologue statement (skip if the body is empty).
2. **[BundleCommand.cs](../src/ScadBundler/BundleCommand.cs)** — extend `--verbose` to report
   "N customizer parameters preserved". (No new severity-level diagnostic for the default path; the
   rearrangement is expected behavior. Collisions on a protected name still surface via existing
   SB5004/SB3003/SB3004.)
3. **Spec** — record the rule in a short `docs/slices/` note (or a "Customizer" section in
   [Diagnostics.md](Diagnostics.md)/UX docs), citing `CommentParser.cc` + `getLineToStop`.

### Tests / goldens

- **`slice5-bundle` corpus case** modeling the demo: a `main.scad` whose first statement is
  `include <lib.scad>`, `lib.scad` carrying a literal global, then root params + an author
  `/* [Hidden] */` + a computed param + a geometry call. Golden asserts: params lead, original
  `/* [Hidden] */` preserved, synthesized fence before the library, library literals after the fence.
  Regenerate with `BLESS_EMIT=1`.
- **Unit (Inliner)**: prologue boundary detection (stops at first def/instantiation; skips leading
  `include`/`use`); empty-prologue still fences; protected param survives a colliding library def
  unrenamed; computed prologue assignment hoisted above its dependency still emits.
- **Idempotence**: the new golden flows through `Golden_IsIdempotentFixedPoint` automatically.
- **Manual acceptance**: re-bundle `ForkedHolder.scad`; confirm OpenSCAD's Customizer shows
  `wall_thickness … tine_count` and nothing from `goews`.

### Risks / edge cases

- ~~**`--minify` / `--no-preserve-comments` drop all comments**, including the fence and the author's own
  `/* [Section] */` — so Customizer grouping is *not* preserved in those modes.~~ ✅ **Fixed**: the inliner
  now marks the comments OpenSCAD's Customizer reads off each hoisted parameter (its `/* [Section] */`
  group header, the description line directly above, and the trailing `// [min:max]` annotation) as
  **sticky**, and the emitter keeps sticky leading *and* trailing trivia under every comment-stripping mode
  (`--minify`/`--obfuscate`/`--no-preserve-comments`). Customizer grouping and labels survive hardening;
  only ordinary comments and the long library headers still drop (the latter via `--strip-license`). See
  `Inliner.StickyCustomizerComments` and [slices/Slice-7-Minify-Obfuscate.md](slices/Slice-7-Minify-Obfuscate.md) §8.
- **Brace-less code between params** (e.g. a bare `cube(10);` or `module m() cube(1);` mid-block):
  our rule stops the prologue at the first instantiation, which can be slightly *more* conservative
  than `getLineToStop` (which only stops at a literal `{`). Real parameter blocks are contiguous and
  precede all geometry, so this is theoretical; note it in the spec.
- **Protected-name vs. genuine library reassignment**: if a library legitimately reassigns a
  prologue name, pinning the prologue means the Customizer default is the visible knob while runtime
  last-wins could differ. Vanishingly rare (libraries don't define `wall_thickness`); documented.

### Implemented (this session)

- [Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs): `ExtractPrologue` (root parameter run),
  `Candidate.Protected` (prologue assignments never renamed/dropped), a protected branch in
  `ResolveGroup` (collisions namespaced on the *other* side), and `Assemble` reordered to emit the
  prologue first then `WithHiddenFence` (synthesized `/* [Hidden] */` as leading trivia on the first
  body statement, only when a body assignment could otherwise leak into the Customizer).
- No new diagnostic code; the rearrangement is expected behavior and round-trips the SB6001 self-check.
- Tests: corpus golden **`slice5-bundle/B-008-customizer-params`** (params + group/annotation comments
  hoisted, fence before the library global); unit tests `CustomizerParameters_HoistedAboveIncludedLibrary_AndFencedWithHidden`,
  `EmptyPrologue_LibraryGlobals_AreStillFencedFromCustomizer`, `RootParameters_PreserveAuthorCustomizerComments`.
  Goldens **B-001**/**B-002** re-blessed (now lead with `/* [Hidden] */`); `CliTests.Bundle_ToStdout_*`
  updated to match.

---

## B. Replicate OpenSCAD's file search paths + `OPENSCADPATH` — v1

### Symptom

The bundler resolves `include`/`use` against the including file's directory plus whatever
`LibraryPaths` it is handed, but does **not** reconstruct the path set OpenSCAD itself would use, so a
project that resolves through the user's OpenSCAD library folder bundles with spurious **SB4001
"can't find …"** where OpenSCAD opens it fine.

### Ground truth

[`parsersettings.cc`](file:///C:/git/hub/openscad/src/core/parsersettings.cc) builds `librarypath` in
`parser_init()`, in order:

1. **`OPENSCADPATH`** split on the platform separator (`;` Windows, `:` POSIX); each entry made
   **absolute** (relative → resolved against CWD; **empty entry → CWD**).
2. **`userLibraryPath()`** = `<documents>/OpenSCAD/libraries`
   ([PlatformUtils](file:///C:/git/hub/openscad/src/platform/PlatformUtils.cc)): Windows = *My
   Documents* (`SHGetFolderPath(CSIDL_PERSONAL)`) → `…/OpenSCAD/libraries`; POSIX =
   `$HOME/.local/share/OpenSCAD/libraries`.
3. **`resourcePath("libraries")`** = the install's bundled libraries (e.g. MCAD).

Per-file resolution `find_valid_path_`: absolute → check directly (`fs::canonical`); else
`<includer dir>/<localpath>`, then each `librarypath` dir in order; first existing non-directory wins.
The bundler's `ResolvePath` ([SourceLoader.cs](../src/ScadBundler.Core/Loading/SourceLoader.cs)) already
matches this **shape** — the gap is only in *assembling* the path list.

### Gaps vs. current behavior

- ❌ **User library folder** (`<Documents>/OpenSCAD/libraries`) not added — main functional gap.
- ❌ **Bundled `resourcePath/libraries`** not added — install-location dependent; best-effort.
- ⚠️ `OPENSCADPATH` entries aren't absolutized and **empty-entry-means-CWD** isn't honored
  ([Bundler.cs](../src/ScadBundler.Core/Inlining/Bundler.cs) + duplicated in BundleCommand).
- ⚠️ `GetFullPath` vs. `fs::canonical` (symlink resolution) — minor; acceptable.

### Design / steps

1. New Core helper `OpenScadEnvironment.LibraryPaths()` (disk/env-facing; **not** used by the
   `IFileSystem` test seam) returning, in order: `OPENSCADPATH` (absolutized; empty → CWD) → user
   library path → resource library path (when locatable). Implement `userLibraryPath()` via
   `Environment.GetFolderPath(SpecialFolder.MyDocuments)` on Windows and `$HOME/.local/share` on POSIX,
   then `/OpenSCAD/libraries`.
2. Effective order: **`-p` entries → `OpenScadEnvironment.LibraryPaths()`** (explicit `-p` overrides
   first, then OpenSCAD's own set). Consolidate the two duplicated `OpenScadPathEntries()` in
   Bundler.cs and BundleCommand.cs into this one helper.
3. Keep `SourceLoader.ResolvePath` as-is (already faithful to `find_valid_path_`).

### Tests

- Unit: `OPENSCADPATH="a;;b"` → `[abs(a), CWD, abs(b)]`; Windows `;` vs POSIX `:`; user-library path
  shape per-OS (mock the env/home).
- Integration (disk): a fixture resolved only via a fake user-library dir resolves with **no SB4001**.
- **Verify** the macOS user-library path against `PlatformUtils-mac.*` if present (Windows path is the
  one that matters for the current user and is exact).

### Implemented (this session)

- New [OpenScadEnvironment.cs](../src/ScadBundler.Core/Loading/OpenScadEnvironment.cs): `LibraryPaths()`
  = absolutized `OPENSCADPATH` entries (empty → CWD) then the per-user library folder
  (`<MyDocuments>/OpenSCAD/libraries` on Windows; `$HOME/.local/share/OpenSCAD/libraries` on POSIX).
  The duplicated `OpenScadPathEntries()` in [Bundler.cs](../src/ScadBundler.Core/Inlining/Bundler.cs)
  and [BundleCommand.cs](../src/ScadBundler/BundleCommand.cs) now both call it. `SourceLoader.ResolvePath`
  was already faithful to `find_valid_path_` and is unchanged.
- Tests: `OpenScadEnvironmentTests` (empty-entry→CWD, relative→absolute, trim, order). Existing
  `CliTests.OpenScadPath_IsHonored` / `LibraryPath_ResolvesUsedLibrary` still pass.
- **Not added**: the install's bundled `resourcePath("libraries")` (location is install-specific —
  supply via `OPENSCADPATH` when needed); macOS path unverified against `PlatformUtils-mac`.

---

## C. Always-namespace `use` imports (default) — v1, **next session**

> **Decision recorded in [ADR 0001](adr/0001-include-use-scoping-and-namespacing.md).** The demo's
> literal "prefix *every* identifier" is **rejected** — it would break `include` (which OpenSCAD merges
> flat/last-wins) and special variables (`$fn` etc., dynamically scoped). The faithful, decided change
> is narrower and is **not** a flag: make `use`-import namespacing unconditional.

### Decision

OpenSCAD evaluates a `use`d library in its own per-file `FileContext` (isolated), so a used library is
*always* in its own namespace — the no-clash case is a coincidence, not the model. Today we namespace
`use`-imports only on a **detected** collision (`ResolveAuto`). Switch the default to namespace **every**
`use`-imported symbol (`<filestem>__name`), rewriting its references (including the root's call sites).
This is "safe by construction" instead of "safe by detection," and changes the *spelling* of library
names but not the *behavior* of any currently-correct bundle.

**Hard exclusions (do not namespace):** `include`-origin definitions (flat/last-wins — namespacing them
diverges from OpenSCAD), special `$`-variables (dynamic scope), and the root's own definitions incl. the
Item-A Customizer prologue.

### Steps

1. [Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs): in `ResolveAuto` (and the singleton path in
   `ResolveGroup`), route **every** non-`Protected` `use`-origin candidate through `NamespaceRep`, not
   just colliding ones. Leave include-origin defs and `$`-vars untouched. Reuse SB5004 (no new code) —
   but consider downgrading the per-symbol diagnostic to Info (or suppressing it) since it now fires for
   every library symbol, not just genuine clashes.
2. Confirm `GatherUseImports` already excludes top-level `$`-special vars (B-002 drops `$fn`); add a
   guard/test if not.

### Tests / risks

- Update `UseImport_NoCollision_KeepsOriginalName` → now expects `lib__box` (deliberate behavior change),
  and re-bless `slice5-bundle/B-002` (the `use` golden) — `WALL`→`lib__WALL`, `box`→`lib__box`. The
  all-`include` goldens (B-001, B-007, B-008, ForkedHolder) are **unaffected** (no `use`).
- Add a golden: two `use`d libs each with a private `helper()` referenced internally → both namespaced,
  each `a()`/`b()` rebinds to its own helper (the isolation case a naive concatenator breaks).
- Add a test for **own-vs-used precedence**: root `module widget(){}` + `use <lib>` also defining
  `widget` → root's wins (OpenSCAD checks own scope first, `ScopeContext.cc:113`).
- **Risk / sequencing**: this reuses the same reference-rewrite path as the cross-`include` mis-bind
  (#4 below). Do **#4 first** (repro-first) so the rewrite is trustworthy, then this.

### Implemented (this session)

- [Inliner.cs](../src/ScadBundler.Core/Inlining/Inliner.cs): the singleton path in `ResolveGroup` now
  namespaces every non-`Protected` `use`-origin candidate via `NamespaceRep` (not only colliding groups),
  reusing the `RenameDeclaration`/reference-rewrite split introduced for #4. A non-clashing import is
  namespaced **silently** (`report: false` → no SB5004; it would otherwise fire per library symbol);
  genuine clashes still warn (`ResolveAuto`/`ResolvePrefix`). `$`-special variables are never `use`
  candidates (the analyzer never records a reference to them, so `PrivateConstants` never reaches one),
  and `include`-origin defs are left flat/last-wins.
- **Diagnostics:** reuse **SB5004** (no new code); the by-construction case is suppressed, the clash case
  stays a Warning. Catalog updated in [Diagnostics.md](Diagnostics.md).
- **Tests:** `UseImport_NoCollision_IsNamespacedForIsolation` (was `…_KeepsOriginalName`),
  `TwoUsedLibraries_PrivateHelpers_StayIsolated` (isolation), `OwnDefinition_ShadowsUsedLibrary_OfSameName`
  (own-vs-used precedence); updated `B002`/`UsedLibrary_InternalReference_FollowsRenamedPrivateConstant`,
  `Slice5EdgeCoverageTests` (`TransitiveUse…`, `Use_ImportsFunctionDefinition`), `BundlerTests`
  (`Bundle_AppendsOpenScadPath…`), and CLI (`LibraryPath_ResolvesUsedLibrary`, `OpenScadPath_IsHonored`).
  Goldens: re-blessed `slice5-bundle/B-002`; added `slice5-bundle/B-009-use-isolation`.

---

## D. Obfuscator (`id_xxxxxx`) — defer to vNext

### Request

Replace every identifier with `id_xxxxxx`, "could just be the memory address of the node."

### Design correction (important)

**Do not use the memory address.** It is non-deterministic across runs and would break golden tests,
emitter idempotence, the SB6001 round-trip, and reproducible output. Use a **deterministic** scheme: a
sequential counter assigned in a stable document-order traversal (`id_000001`, `id_000002`, …), or a
short hash of `(file, name, ordinal)`. With that swap, the obfuscator is just Item C with a different
name-*generator*: same candidate set, same reference rewrite, same prologue exemption — only
`NamespaceRep`'s name function changes.

### Disposition

Defer to **vNext** per the user. When built: `--obfuscate` (off by default), deterministic ids, prologue
exempt, reuse SB5004, golden proving stable output across two runs. Building C first makes D a thin
addition.

---

## Diagnostic codes

- **Item A**: no new severity-level code for the default rearrangement; collisions on a protected name
  reuse SB5004/SB3003/SB3004. (If a `--verbose`-only Info marker is later wanted, catalog it in
  [Diagnostics.md](Diagnostics.md) first — **SB5007 is now claimed** by license aggregation
  ([Post-v1-Plan.md](Post-v1-Plan.md) #2, done); next free is **SB5008**.)
- **Items C/D**: reuse **SB5004 (NameRenamed)** — no new codes.

## Recommended sequence

1. ~~**A — Customizer parameters**~~ — ✅ done.
2. ~~**B — Search-path fidelity**~~ — ✅ done.
3. ~~**[#4 — cross-`include` mis-bind]**~~ ([Post-v1-Plan.md](Post-v1-Plan.md) #4) — ✅ done (prerequisite
   for trustworthy reference rewriting in C).
4. ~~**C — always-namespace `use` imports**~~ (default; [ADR 0001](adr/0001-include-use-scoping-and-namespacing.md)) — ✅ done this session.
5. **D — Obfuscator** (vNext; deterministic ids; thin layer over C).
