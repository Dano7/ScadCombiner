# ADR 0001 — `include`/`use` scoping and the identifier-namespacing policy

- **Status:** Accepted — 2026-06-09 · **Implemented** (Decision 2, always-namespace `use`) — 2026-06-09
- **Deciders:** Dan Olsen (power-user demo feedback) + Claude
- **Supersedes/affects:** the framing of "Item C" in [Post-Demo-Plan.md](../Post-Demo-Plan.md); informs
  [Post-v1-Plan.md](../Post-v1-Plan.md) #4.

## Context

The demo raised the question: *to keep the bundle "understood the same as when OpenSCAD reads the user
file and imports the libraries," shouldn't we prefix **every** identifier with its file name so two
same-named items in different files can't collide?* The worry: if combining files into one drops the
per-file scoping, accidental name collisions silently change behavior and we degrade into a naive
concatenator.

The principle is correct (it is the Constitution's "semantically equivalent" rule). The mechanism,
however, must be **origin-dependent**, because OpenSCAD's two import statements have *opposite* scoping —
verified in the OpenSCAD source (`C:\git\hub\openscad`, `openscad-2019.05-3933-g6b81cb63e`):

- **`include` is textual, one shared scope.** It is handled in the *lexer*
  ([`lexer.l` lines ~139–148](file:///C:/git/hub/openscad/src/core/lexer.l)): on `include <f>` the lexer
  opens `f` and splices its tokens into the current stream. There is **no `include` rule in
  `parser.y`** — included content is parsed into the *same* `LocalScope` as the includer, and duplicates
  are **last-wins** (`LocalScope.cc`). Included names are *not* scoped to their file. Cross-file
  references work freely (e.g. the demo's `cleat_spacing_x = goews_staggered_x_spacing` reaches into the
  included `goews` library's global — that only resolves because `include` shares scope).
- **`use` is isolated, separate scope.** `use <f>` only records `f` in `usedlibs`
  ([`parser.y:179`](file:///C:/git/hub/openscad/src/core/parser.y)). At evaluation,
  `FileContext::lookup_local_{function,module}`
  ([`ScopeContext.cc:83–134`](file:///C:/git/hub/openscad/src/core/ScopeContext.cc)) finds the symbol in
  the *used* file's scope and runs it in a **fresh `FileContext` for that library, parented only to the
  builtins** — never the caller. `use` imports modules/functions only (never variables); a used
  library's body sees *its own* globals/helpers, and the caller's globals never leak in.
- **Special variables (`$fn`, `$fs`, `$t`, …) are dynamically scoped** — they intentionally propagate
  from the call site into callees, including across a `use` boundary. They are the one identifier class
  that *must* stay shared.

## Decision

1. **`include` content is flat-merged with last-wins and is never namespaced.** This is the faithful
   reproduction of OpenSCAD's lexer-level inclusion. Prefixing included names would *diverge* from
   OpenSCAD (it would split a single last-wins variable into several, and break cross-`include`
   references). `include` collisions are reproduced (and surfaced as SB3003/SB3004 warnings), not
   "fixed."

2. **`use`-imported symbols are always namespaced** (`<filestem>__name`), *by construction* — not only
   when a collision is detected. This matches OpenSCAD's per-file `FileContext` isolation exactly and
   removes any dependence on collision-detection completeness ("safe by construction" rather than "safe
   by detection"). Their references (including the root's call sites) are rewritten to match. This
   changes the **spelling** of library names but not the **behavior** of any currently-correct bundle.

3. **Special variables are never namespaced** and never imported as `use` private constants. Renaming
   `$fn` etc. would break dynamic scoping (e.g. faceting a cylinder differently). This is a hard
   exclusion that any "prefix everything" scheme would violate — and a primary reason a literal
   prefix-everything policy is rejected.

4. **The root file's own definitions are never namespaced** — including the Customizer parameter
   prologue (see [Post-Demo-Plan.md](../Post-Demo-Plan.md) Item A). The end user reads and edits these.

5. **The cross-`include` reference mis-bind under non-`Auto` strategies is a real bug to fix**
   ([Post-v1-Plan.md](../Post-v1-Plan.md) #4): under `--on-collision prefix|keep-first|keep-last`, a
   reference can rebind to the wrong duplicate because the pre-inline model binds to last-wins. The
   default `Auto` path is correct; this is the one place the bundler can actually mis-bind today, and it
   gates any future include-prefixing feature.

## Alternatives considered

- **Prefix every identifier (the demo's literal suggestion).** Rejected: it breaks `include` semantics
  (splits last-wins variables; breaks cross-`include` references like the demo's own file) and breaks
  special-variable dynamic scoping. It would make the bundler *less* faithful, not more.
- **Namespace `use`-imports only on detected collision (the prior default).** Correct for any given
  bundle's contents and produces cleaner names, but it is "safe by detection." Decision 2 supersedes it
  for robustness and fidelity to the `FileContext` model.

## Consequences

- The bundler is demonstrably **not** a naive concatenator: it flat-merges `include` (faithful) and
  isolates `use` (namespaced) — a distinction only possible because it parses (a text concatenator
  cannot tell a `use`-import from an `include`d name from a local).
- Bundles gain noisier library names (`lib__widget`) even when unambiguous; acceptable for the
  upload-oriented output, and zero impact on all-`include` projects (e.g. the demo's `ForkedHolder`).
- The vNext obfuscator and any hypothetical include-prefixing both reuse the reference-rewrite path, so
  both are gated behind the #4 fix.

## Implementation pointers

- `use` always-namespace: **done (2026-06-09).** The singleton path in
  [Inliner.cs](../../src/ScadBundler.Core/Inlining/Inliner.cs) `ResolveGroup` now routes every
  non-`Protected` `use`-origin candidate through `NamespaceRep` (not only colliding groups). A
  non-clashing import is namespaced **silently** (`report: false` → no SB5004; it would otherwise fire
  per library symbol); genuine clashes still warn via the collision paths (`ResolveAuto`/`ResolvePrefix`).
  `$`-vars are never candidates (the analyzer never records them) and include-origin defs are untouched.
  Tests: `B002`/`UseImport_NoCollision_IsNamespacedForIsolation`/`TwoUsedLibraries_PrivateHelpers_StayIsolated`/
  `OwnDefinition_ShadowsUsedLibrary_OfSameName`; goldens `slice5-bundle/B-002` (re-blessed) and
  `B-009-use-isolation`.
- #4 mis-bind: **done (2026-06-09)** — the prerequisite reference-rewrite fix; see
  [Post-v1-Plan.md](../Post-v1-Plan.md) #4 "Resolved".
