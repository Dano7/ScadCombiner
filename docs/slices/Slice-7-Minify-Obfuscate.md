# Slice 7 — Minifier & Obfuscator (`--minify` AST stage / `--obfuscate`)

**Status**: ✅ **Implemented** (2026-06-11). Code in `src/ScadBundler.Core/Transforming/`; differential
fixture `tests/Corpus/integration/T-001-harden` proves byte-identical CSG for both profiles against the
official binary. Supersedes
[Post-Demo-Plan.md](../Post-Demo-Plan.md) **Item D** (the `id_xxxxxx` obfuscator sketch) and promotes
the emitter's textual `--minify` (Slice 6) to a full AST-level minification stage. Builds directly on the
existing rename/reference-rewrite machinery ([Inliner.cs](../../src/ScadBundler.Core/Inlining/Inliner.cs)
`NamespaceRep`/`RenameDeclaration`), the `Protected` Customizer-prologue exemption, the `ISemanticModel`
reachability queries, and the OpenSCAD differential harness (`tests/ScadBundler.IntegrationTests`).

---

## 1. Goals & non-goals

Two output-hardening features that share one engine and one safety model:

- **Minifier** (`--minify`, enhanced) — **minimize the byte size** of the bundle. Incidentally unreadable.
- **Obfuscator** (`--obfuscate`, new) — **maximize the cost of reverse-engineering** the bundle.
  Output may be *larger* than the input.

Both are governed by one **non-negotiable correctness bar** (§2) and resolved as **two mutually
exclusive profiles over a shared transform engine** (§4). Decisions locked with the user (2026-06-10):

| Decision | Choice |
|---|---|
| Equivalence guarantee | **Tier 1 only** — CSG-tree-preserving transforms, provable via the byte-identical CSG harness. **No geometry kernel.** |
| v1 transform set | **Safe high-value set first** — no arithmetic evaluation, no control-flow rewriting (both deferred; §12). |
| Profile relationship | **Two profiles, shared engine, mutually exclusive** — `--minify` and `--obfuscate` together is a CLI error (exit 2). |
| Determinism | **Deterministic with avalanche, always on (no escape hatch)** — a one-character source change reshuffles *every* generated name (§5). Never memory addresses. |
| Customizer params | Names survive **once** at the top (for OpenSCAD's Customizer), aliased to a generated id used everywhere after (§7). |
| License in hardened output | **Kept by default** in both profiles; `--strip-license` opts out (§8). |
| Obfuscation surface | **A single plain `--obfuscate` flag** for v1 — strength/style knobs deferred (§12). |

**Non-goals (this slice):** any Tier-2 transform (solid-equivalent CSG *restructuring*); a partial
evaluator / constant folder; control-flow rewriting (unroll, `if`↔`?:`); a geometry/mesh model. See
§12 for why, and what each would require.

---

## 2. The equivalence model — what "identical geometry" means here

OpenSCAD source is **a program that evaluates to a CSG tree**, not a geometry description. The project's
correctness bar is already defined operationally by the differential harness: **byte-identical `.csg`
output from the official binary, identical `ECHO:` output, and no new warning-class stderr**
(`tests/ScadBundler.IntegrationTests/DifferentialAssert`). That bar is **stricter than "same solid."**

This slice transforms only in the space that bar can *prove*:

- **Tier 1 — CSG-tree-preserving** *(in scope).* Changes *how the source computes values and structures
  calls* while producing the **exact same CSG tree** (same nodes, same params, same child order). Stays
  entirely in the evaluation/value domain. The existing harness is the proof.
- **Tier 2 — solid-preserving, CSG-restructuring** *(out of scope).* Reordering commutative unions,
  `cube`→equivalent `polyhedron`, alternate boolean factorings. Same object, **different CSG bytes** →
  fails the harness. Verifying these needs an exact geometry kernel and even then "guarantee" degrades to
  tolerance-based mesh comparison (floating point). Rejected — it trades a *provable* feature for an
  *approximately-correct* one, against the Constitution's "correctness over cleverness" and "minimal
  dependencies."

**Invariants every transform in this slice must preserve** (the operational definition of Tier 1):

1. **Value identity** — every value that feeds a geometry parameter evaluates to the **bit-identical**
   IEEE-754 double / vector / string / boolean it did before. (We do not *evaluate* anything in the
   safe set, so this holds by construction; it becomes the gate for any future folding — §11.)
2. **CSG node & child-order identity** — the set, shape, and *order* of instantiated nodes is unchanged.
   Top-level statement order and module-children order are therefore **pinned** (OpenSCAD unions children
   in order; the `.csg` records that order). No reordering of executed statements.
3. **Side-effect identity** — `echo`/`assert` evaluation, and their order, is unchanged (the harness
   compares `ECHO:` output ordered). No top-level `echo`/`assert`, and no assignment whose RHS contains an
   `echo`/`assert`, may be dropped or moved.
4. **Special-variable & dynamic-scope identity** — `$fn`, `$fa`, `$fs`, `$t`, `$children`, … are never
   renamed, aliased, or hoisted (dynamic scope; [ADR 0001](../adr/0001-include-use-scoping-and-namespacing.md) §3).
5. **Customizer identity** — the set of parameters OpenSCAD's Customizer extracts, their names, groups,
   and annotations, is unchanged (names §7, group/description/annotation trivia §8; rule mirrors
   `CommentParser::collectParameters`).

---

## 3. Why this is not a half-measure

Tier 1, restricted to the safe set, is far richer than "rename identifiers + strip whitespace":

- **Dead-code elimination / tree-shaking** (§6.2) deletes whole unreferenced definitions — on a bundle of
  a large library where three functions are used, this is the single biggest size win, and it removes the
  meaningful names an attacker would anchor on. Provably CSG-inert (unreachable code emits no CSG/echo).
- **Indirection injection** (§6.5) and **dead-code injection** (§6.6, incl. the disable-modifier `*`
  trick) make the obfuscated output genuinely costly to undo — control-flow-inert noise that *renders to
  nothing* yet looks load-bearing.
- **Parameter aliasing** (§7) consumes every meaningful Customizer name into an opaque id after a single
  required occurrence.
- **Avalanche** (§5) defeats diffing between versions.

What it deliberately does **not** claim is arbitrary "substitute any equivalent operation" — because under
the byte-identical bar most such substitutions are unverifiable (Tier 2). The safe set is the maximal
*provable* subset, which is the honest reading of "no half measures."

---

## 4. Architecture — one engine, two profiles

A new stage **`Transforming/`** runs **after the Inliner, before the Emitter**:

```
SourceLoader → Lexer → Parser → SemanticAnalyzer → Inliner → [Transformer] → Emitter
```

```csharp
// Transforming/IBundleTransform.cs
internal interface IBundleTransform
{
    string Name { get; }
    ScadFile Apply(ScadFile bundle, TransformContext ctx);
}
```

- **`TransformContext`** carries: the `DiagnosticBag`, the post-inline `ISemanticModel` (re-run on the
  flattened bundle, since the inliner already rewrote names), the `NameGenerator` (§5), the
  `Protected`/prologue node set forwarded from the inliner, and the active `TransformProfile`.
- **`Transformer.Run(bundle, profile, ctx)`** applies the profile's ordered pass list. Each pass is an
  AST→AST rewriting visitor that rebuilds changed subtrees with `with` (AST stays immutable;
  reference-identity side tables per [AST-Reference.md](../AST-Reference.md) §15.6).
- **`TransformProfile`** = an ordered `IReadOnlyList<IBundleTransform>` + a `NameGenerator` factory. Two
  built-ins: `Minify`, `Obfuscate`. They are **mutually exclusive** at the CLI.

**Pass order — Minify:** `ParameterAliasing` → `DeadCodeElimination` → `LiteralCanonicalization` →
`IdentifierRenaming(short, permuted)`. Then emit with textual-minify (Slice-6 `--minify`) **keeping the
aggregated license header** (§8).

**Pass order — Obfuscate:** `ParameterAliasing` → `DeadCodeElimination` →
`IdentifierRenaming(opaque, hashed)` → `IndirectionInjection` → `DeadCodeInjection` →
`StringDecomposition`. Then emit dropping per-section banners and ordinary comments but **keeping the
aggregated license header** (§8).

Ordering rationale: aliasing before DCE (the alias keeps the prologue name reachable); DCE before renaming
(don't rename what you'll delete); injection after renaming (injected nodes are generated already-opaque).

The shared engine means the safety-critical reference-rewrite path exists **once**. Each `IBundleTransform`
is independently unit-tested *and* carries a differential-harness fixture (§9).

---

## 5. Determinism & avalanche

**Seed.** Compute `H = FNV1a64(canonicalText)` where `canonicalText` is the post-inline bundle emitted
with default `EmitOptions` (deterministic, already idempotent). `H` is the global seed. *Any* source change
that survives into the bundle perturbs `canonicalText` → flips `H` → reshuffles every generated name.

**Name generator.** Renamable declarations are enumerated in a **stable document-order traversal**, each
assigned an ordinal `i`. Then:

- **Obfuscate**: `name(i) = "_" + Base32(mix(H, i))` (fixed width, e.g. `_a7f3k9`), drawn from a
  collision-checked pool (reuse `UniqueName`). (A misleading-name `--obfuscate-style` variant is
  deferred — §12.)
- **Minify**: assign the shortest-available identifiers `a, b, …, z, aa, …` — but in an order **permuted by
  `H`** (`order = argsort(mix(H, i))`). The *set* of names and the total byte count stay minimal; *which*
  declaration gets `a` avalanches. This reconciles avalanche with minimal size.

**Never** use memory addresses / hash codes of live objects — non-deterministic across runs; breaks
goldens, idempotence, and the SB6001 round-trip ([Post-Demo-Plan.md](../Post-Demo-Plan.md) Item D
"Design correction").

**Idempotence.** Running a profile twice on the same input yields byte-identical output (same `H`, same
ordinals). Running a profile on already-transformed output is **not** required to be a fixed point (names
are already opaque; re-running re-opaquifies deterministically) — but it must still round-trip (SB6001)
and stay CSG-identical.

---

## 6. Transform catalog (v1 safe set)

Each entry: **what · why Tier-1-safe · profile(s) · example.** "Safe" claims are *hypotheses validated by
a differential-harness fixture* (§9) before the transform ships — never assumed.

### 6.1 Identifier renaming  ·  both
- **What.** Rename user modules, functions, top-level/closure variables, parameters, and `let`/`for`
  bindings via the existing `RenameDeclaration` + reference-rewrite, using the §5 generator.
- **Safe.** Names are arbitrary; references are rewritten consistently. Never touches built-ins,
  `$`-special vars, the Customizer prologue (`Protected`), or imported-file *path* strings.
- **Note.** Parameter/binding renames are scoped — rename a parameter and only its in-scope uses, not a
  same-named global. The post-inline `ISemanticModel` provides the scoped `ReferencesTo`.

### 6.2 Dead-code elimination (tree-shaking)  ·  both
- **What.** Remove top-level `ModuleDefinition`/`FunctionDefinition` never (transitively) referenced from a
  **root**, and top-level assignments that are unreferenced **and** side-effect-free.
- **Roots** (must be kept; seed reachability from these): every executed top-level statement
  (`ModuleInstantiation`, `If/For/IntersectionFor/Let` statements, `BlockStatement`, top-level `echo`/
  `assert` calls), **every Customizer prologue assignment** (a knob is user-facing even if unread), **and
  every `$`-special-variable assignment** (see below).
- **Removable assignment rule.** Drop a top-level `AssignmentStatement` only if (a) its name has zero
  references after DCE of definitions, (b) its RHS subtree contains **no** `EchoExpression`/
  `AssertExpression` (those fire at top-level evaluation and the harness compares `ECHO:`), **and (c) its
  name is not a `$`-special variable**. Never drop a prologue/`Protected` assignment.
- **Special variables are never tree-shaken** (`Builtins.IsSpecialVariable`). A top-level `$foo = …`
  establishes a **dynamically-scoped** default that any module reached at render time may read off the call
  stack. Those reads bind to *no* symbol in the static model (special variables are not lexically scoped),
  so the mark-and-sweep can never observe the edge and would wrongly drop the default — leaving the dynamic
  read `undef`. This silently broke real BOSL2 projects under `--minify`/`--obfuscate` (the
  `$tags_shown = "ALL"` / `$transform = IDENT` / `$parent_gear_*` attachment globals vanished, so an
  attachment `assert` failed). We cannot prove a dynamic read absent, so the assignment is always kept.
  Covered by the `T-001-harden` differential fixture (a `$ribs` default read through a module loop).
- **Safe.** OpenSCAD has no string/dynamic dispatch of module or function names (no `eval`), so static
  reachability of definitions is sound; unreachable definitions instantiate nothing → no CSG, no echo.
  Definitions do not execute at definition time (only at call), so removing an unreferenced one drops no
  side effect. The special-variable exception above closes the one case where a *value* read is invisible
  to the static model.
- **Reuses.** `ISemanticModel` references + the `PrivateConstants` reachability shape already built for
  `use` imports.

### 6.3 Number / literal canonicalization  ·  minify
- **What.** Replace each `NumberLiteral.RawText` with the **shortest string that lexes to the identical
  `double`** (`1.0`→`1`, `0.5`→`.5`, `2.0e3`→`2e3`, `0x10`→`16` when shorter). Strip redundant `+`/leading
  zeros. Collapse `BooleanLiteral`/`UndefLiteral` spelling (already canonical).
- **Safe.** Chosen spelling parses to the same `double` (verified by re-lexing the candidate and comparing
  bits) — **no arithmetic is performed**. This is re-spelling, not folding.
- **Guard.** If no shorter exact spelling exists, keep `RawText` verbatim. Numbers flagged SB1007
  (imprecise) are left untouched.

### 6.4 Parameter aliasing  ·  both  →  **see §7** (the Customizer requirement).

### 6.5 Indirection injection  ·  obfuscate
- **What.** Add reference-transparent wrappers: (a) alias chains for renamed globals (`_a = _b; _b = _c;`
  where reads hop the chain — all side-effect-free, all post-fence); (b) identity-function wraps of
  arbitrary sub-expressions: `e` → `(function(_x) _x)(e)`; (c) `let`-wraps: `e` → `let(_q = e) _q`.
- **Safe.** An identity-function application and a `let` binding are reference-transparent for **any** value
  type (scalar, vector, string, range, function-value) — no type assumptions (contrast `e+0`/`e*1`, which
  error on vectors; **forbidden**). Wrapping never changes evaluation order of `echo`/`assert` because
  wrappers are applied to already-pure sub-expressions only (skip any subtree containing `echo`/`assert`).
- **Budget.** A **fixed internal density** bounds output growth in v1 (no knob; the `--obfuscate-strength`
  budget is deferred — §12).

### 6.6 Dead-code injection  ·  obfuscate
- **What.** Inject plausible-looking, render-inert noise: (a) uncalled `module`/`function` definitions with
  opaque bodies; (b) **disable-modifier subtrees** — `*` makes a subtree contribute nothing to geometry
  (lexically equivalent to commenting it out), so `*<generated geometry>;` is CSG-inert decoy; (c) dead
  `let`/local bindings.
- **Safe.** `*` (Disable) is the clean primitive — verified CSG-inert. **Do not** use `%` (Background) or
  `#` (Highlight) for injection: `%` *can* affect preview/`.csg` background output; `#` changes
  highlighting. Only `*` is used. Injected definitions are unreferenced (DCE-equivalent inert).
- **Determinism.** Injected names/shapes are drawn from `H` (§5).

### 6.7 String decomposition  ·  obfuscate
- **What.** Rewrite string literals as equivalent constructions: `"abc"` → `str(chr(97), chr(98), chr(99))`
  or `str("a","b","c")`, mixing forms by `H`.
- **Safe.** `str`/`chr`/`concat` over integer codepoints and literals are deterministic and
  platform-independent (integer→codepoint, no libm). Re-decoded value is bit-identical. Skip strings used
  in **path/font-sensitive** positions: `import(file=…)`, `surface(file=…)`, `text(font=…)`,
  `use`/`include` paths — those must stay literal (font/file resolution and the harness's font pass-through).

### 6.8 Trivia handling  ·  both  →  **see §8** (license preservation).

---

## 7. Customizer parameter aliasing (the headline requirement)

**Requirement.** The Customizer parameters at the top keep their **original names — but only at the top**.
That name must not recur through the bundle: each parameter is assigned to a shorter/opaque id
*immediately*, and that id is used everywhere after.

**Why it's safe & how it satisfies the Customizer rule.** OpenSCAD's `CommentParser::collectParameters`
extracts a knob iff the assignment is (1) a **literal**, (2) **before the first `{`**, (3) physically in
the **root** file. The Inliner's Item-A prologue hoist already places the root's literal params first,
verbatim, fenced by a synthesized `/* [Hidden] */`. This transform leaves those untouched (so all three
rules still select exactly them) and adds, **immediately after the fence**, one *computed* alias per
parameter:

```scad
// ── prologue (untouched; Customizer sees these) ──
wall_thickness = 6;        // [1:20]
tine_count = 4;
/* [Hidden] */             // synthesized fence (Item A)
// ── aliases (computed ⇒ never themselves Customizer params; rule 1) ──
_k3 = wall_thickness;
_p9 = tine_count;
// ── body: every read of wall_thickness/tine_count rewritten to _k3/_p9 ──
```

- The alias RHS is the **one** post-fence occurrence of the meaningful name. Every **body** reference is
  rewritten to the alias (reuse the reference-rewrite path; the prologue node itself is `Protected` and
  never renamed).
- Aliases are **computed** assignments, so rule (1) guarantees they never appear as Customizer knobs —
  double-safe with the `/* [Hidden] */` fence (rule 2 boundary).
- Document order is correct: prologue → fence → aliases → body; every alias read follows its definition, so
  the **SB5008** forward-reference checker stays green.
- `$`-special vars are never parameters and never aliased (§2 invariant 4).
- Both profiles use this; the alias *name* comes from the §5 generator (short for minify, opaque for
  obfuscate).

**Edge cases.** A *computed* root assignment interleaved in the prologue (e.g. `cleat_spacing_x = …`,
already kept at document position by Item A) is not a knob and is renamed/aliased like any body global. A
parameter unreferenced in the body still gets no alias emitted (nothing to rewrite) but its prologue line
stays (it remains a knob).

---

## 8. Trivia & license preservation

- **License block is preserved by both profiles.** The Attribution pass ([Attribution.cs](../../src/ScadBundler.Core/Inlining/Attribution.cs))
  aggregates every bundled file's header/license run to the top (default on). Legal text must survive a
  hardened bundle — the audience is a MakerWorld/Thingiverse downloader. Obfuscate **drops per-section
  provenance banners** and all other comments but **keeps the aggregated license header**; minify likewise
  keeps the license header.
- **This changes Slice-6 textual `--minify` behavior** (which dropped *all* comments incl. license). The
  new AST `--minify` stage marks the aggregated-license trivia **non-strippable**, and the emitter honors
  that mark even under textual minify. (Recommended; flagged in §12 as the one behavior change to confirm.)
  A `--strip-license` opt-out is provided for users who own all sources.
- **Customizer-structural trivia** (`/* [Group] */`, the description line, `// [min:max]`, and the
  synthesized `/* [Hidden] */`) is preserved by **both** profiles, and **even under explicit
  comment-stripping** (`--minify`/`--obfuscate`/`--no-preserve-comments`). The inliner marks exactly the
  comments OpenSCAD's `CommentParser` reads off each hoisted parameter as sticky (its group header, the
  description directly above, and the trailing annotation), and the emitter honors that mark — so the
  Customizer grouping and labels are *not* lost under hardening (§2 invariant 5). Only ordinary comments
  and the long library headers drop (the latter via `--strip-license`). This is the surgical alternative
  to keeping all comments, which would re-bloat the bundle with the very headers hardening is meant to shed.

---

## 9. Safety methodology — how each transform earns its place

1. **Whitelist.** Only transforms in §6/§7, each argued Tier-1-safe against §2's invariants.
2. **Per-transform differential fixture.** Every transform ships a `tests/Corpus/integration/T-NNN-*`
   fixture exercised by `tests/ScadBundler.IntegrationTests`: render original vs. transformed bundle
   through the official binary, assert **byte-identical `.csg`**, identical `ECHO:`, no new warnings. A
   transform whose "safe" hypothesis fails here (e.g. an unexpected implicit-group CSG difference) is
   **not shipped** until narrowed. This is the empirical backstop that makes the Tier-1 claim real.
3. **In-process round-trip.** The existing **SB6001** structural self-check runs on transformed output
   (re-parse + `StructuralKey`); transformed bundles must round-trip.
4. **Idempotence/determinism goldens** (§10): two runs byte-identical; a one-char source edit avalanches
   the name set (assert the diff is "large" — e.g. ≥90% of generated names changed).
5. **Property test.** For renaming + aliasing + indirection: a fuzz corpus asserts the post-transform
   `ISemanticModel` binds every reference to the same *definition identity* as pre-transform (semantic
   no-op), independent of OpenSCAD availability.

---

## 10. CLI / UX

- `--minify` — now drives the AST minify profile **and** the emitter's textual minify. (Back-compatible
  flag; behavior strengthened.)
- `--obfuscate` — new; a single plain flag selecting the obfuscate profile (default comment policy = keep
  license, drop the rest). Strength/style knobs are deferred (§12).
- `--strip-license` — opt out of license preservation (§8).
- **Mutual exclusion**: `--minify --obfuscate` → CLI error, **exit 2**. Document in [UX.md](../UX.md).
- `--verbose` → **SB5009** summary (§11).
- `--dry-run`/`--diff` compose unchanged.

Wire in [BundleCommand.cs](../../src/ScadBundler/BundleCommand.cs); thread the profile through
`BundleOptions` → `Bundler.Bundle` → the new `Transformer`. Update [UX.md](../UX.md) options table and
the "Collision Strategies" neighborhood.

---

## 11. Diagnostics

| Code | Sev | Trigger | Message (template) |
|---|---|---|---|
| **SB5009** | Info | a hardening profile ran (`--verbose`) | `{profile}: {renamed} identifiers renamed, {removed} definitions tree-shaken, {aliased} customizer params aliased{, injected} ({deltaBytes:+#;-#} bytes).` |
| **SB5010** | Info | *(reserved)* a transform was skipped by a safety guard | `{transform} skipped on {node}: {reason}.` |

Catalog both in [Diagnostics.md](../Diagnostics.md) **before** implementation (the "never invent a code at
implementation time" rule). SB5010 is reserved now for the deferred folding/control-flow guards (§12); the
v1 safe set rarely skips, but DCE may surface it (e.g. a genuinely ambiguous case → keep, don't drop).

---

## 12. Out of scope (deferred — and what each would need)

- **Tier-2 solid-equivalent restructuring** (union reorder, `cube`→`polyhedron`, boolean refactor).
  Needs an exact geometry kernel + tolerance-based mesh equivalence; cannot ride the byte-identical
  harness; rejected for v1 (§2).
- **Constant folding / partial evaluation.** Deferred. When done, it requires a **bit-exact** OpenSCAD
  evaluator and a strict whitelist: fold only small-integer `+`/`-`/`*`, unary `±`, boolean ops, literal
  `str`/`chr`/`concat`, literal vector build. **Never** fold transcendentals (`sin`/`cos`/`sqrt`/`pow` —
  libm mismatch vs .NET), division/modulo (inexact), or **reassociate** float ops (`(a+b)+c ≠ a+(b+c)`);
  commute `+`/`*` only (bit-exact). Gated by §9's per-fixture harness and SB5010 skips.
- **Control-flow rewriting** (loop unroll, `if`↔`?:`, flatten). Deferred. Each is *plausibly* Tier-1 but
  must be proven per-shape against the harness (e.g. confirm an unrolled `for` produces the byte-identical
  implicit-group CSG, and the range evaluates bit-exactly). Promote individually once a fixture is green.
- **Obfuscation knobs.** `--obfuscate-strength {1..3}` (indirection/injection density), `--obfuscate-style
  {opaque,confusing}` (name pool), and a `--stable-names` escape hatch that would disable the avalanche
  permutation for clean minified diffs. **All deferred** — v1 ships a single plain `--obfuscate` with a
  fixed internal density and the opaque name pool, and **avalanche is always on** (user decision,
  2026-06-10; §15).

---

## 13. Scope (In / Out) & file layout

**In:** `Transforming/` stage (`IBundleTransform`, `Transformer`, `TransformContext`, `TransformProfile`,
`NameGenerator`, the §6/§7 transforms); CLI flags (§10); SB5009/SB5010 catalog; license-preservation mark
on aggregated trivia (§8); corpus + integration fixtures (§9).

**Out:** §12 deferrals; emitter line-length wrapping; WASM/JSON surface (the engine is consumable by it
later, dependency-free).

```
src/ScadBundler.Core/
  Transforming/   IBundleTransform.cs · Transformer.cs · TransformContext.cs · TransformProfile.cs
                  NameGenerator.cs
                  IdentifierRenaming.cs · DeadCodeElimination.cs · LiteralCanonicalization.cs
                  ParameterAliasing.cs · IndirectionInjection.cs · DeadCodeInjection.cs
                  StringDecomposition.cs
```

---

## 14. Exit criteria

- [ ] Zero-warning build; `dotnet test` green; new code ≥95% line coverage (per Constitution).
- [ ] `--minify` and `--obfuscate` selectable and **mutually exclusive** (exit 2 together).
- [ ] **Determinism**: a golden proves two runs of each profile are byte-identical.
- [ ] **Avalanche**: a test proves a one-character source edit changes ≥90% of generated names.
- [ ] **Customizer aliasing**: golden shows prologue names verbatim at top, `/* [Hidden] */` fence,
      computed aliases after, all body references rewritten; OpenSCAD Customizer still lists the original
      knobs (manual acceptance on `C:\git\dan\SCAD\ForkedHolder.scad`).
- [ ] **Tree-shaking**: golden where an unused library definition is dropped; SB5009 reports it.
- [ ] **License preserved**: obfuscated & minified bundles retain the aggregated license header; banners
      dropped under obfuscate.
- [ ] **Differential**: each transform's `T-NNN-*` integration fixture renders **byte-identical CSG**,
      identical `ECHO:`, no new warnings against the official binary (skips cleanly when OpenSCAD absent).
- [ ] SB6001 round-trip holds on all transformed goldens; new diagnostics cataloged in
      [Diagnostics.md](../Diagnostics.md) first.

---

## 15. Resolved decisions (confirmed 2026-06-10)

1. **License under `--minify`/`--obfuscate`.** The aggregated license header is **non-strippable by
   default** in *both* profiles (changing Slice-6's textual `--minify`, which dropped it); `--strip-license`
   opts out. **Confirmed** — maker-trust, aligns with the default-on Attribution pass.
2. **Obfuscation surface.** Ship a single plain **`--obfuscate`** with a fixed internal density and the
   opaque name pool; `--obfuscate-strength`/`--obfuscate-style` are **deferred** (§12). **Confirmed.**
3. **Minify name reset vs. avalanche.** The permuted-short-name scheme (§5) is adopted; **avalanche is
   always on with no escape hatch** (no `--stable-names`). Minified diffs are intentionally unstable across
   edits. **Confirmed.**
