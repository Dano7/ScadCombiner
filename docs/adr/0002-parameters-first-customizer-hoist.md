# ADR 0002 — `--parameters-first`: hoist Customizer parameters above the license header

- **Status:** Accepted — 2026-06-24 · **Implemented** — 2026-06-24
- **Deciders:** Dan Olsen (Thingiverse upload experience) + Claude
- **Affects:** [UX.md](../UX.md) (Primary Options + a new "Platform compatibility" note), the
  `Inliner` assembly phase, and the `DeadCodeElimination` hardening pass.

## Context

Bundles produced by ScadBundler render correctly on MakerWorld and generally on Thingiverse. But
Thingiverse's Customizer is **out of spec** in (at least) one observable way: when a long run of
comments precedes the first Customizer parameter, its parameter parser fails to surface the
parameters at all. The default bundle layout puts exactly such a run there — the aggregated
license/attribution header (`--bundle-licenses`, default on) is hoisted to the **very top**, *above*
the Customizer parameter prologue:

```
// <root header>
// ======== file headers & licenses aggregated by ScadBundler ========
//   …every bundled file's license, deduplicated…
// ====================================================================
/* [Size] */
width = 10;          // ← the first real parameter, now far down the file
…
```

Manually moving the parameters *above* the license header makes Thingiverse display every parameter
correctly. We want a flag that does this automatically.

This is purely a **platform-compatibility workaround for an out-of-spec parser**, not a correctness
fix and not a better default — so it must be **opt-in** and documented as such. (Thingiverse's separate
~1–2 s render-time limit is *not* addressed here; that is a model-complexity problem outside this
tool's scope. `--minify` is the only lever, and it composes with this flag.)

## Decision

Add an opt-in `--parameters-first` flag (`BundleOptions.ParametersFirst`, default `false`). When set,
the inliner emits the aggregated license/header block **immediately after** the Customizer parameter
prologue (above the rest of the body) instead of above the parameters:

```
/* [Size] */
width = 10;          // ← parameters now lead the file
…
// <root header>
// ======== file headers & licenses aggregated by ScadBundler ========
//   …
// ====================================================================
/* [Hidden] */
…body…
```

Key properties:

1. **Comment-relocation only — Tier-1 safe.** The inliner already hoists the literal parameter
   prologue to the top and protects it (never renamed, never dropped); the only thing that moves is the
   attachment point of the header *trivia*. No statement reorders, so the rendered CSG is
   **byte-identical**. The flag changes where a block of comments is emitted, nothing else.

2. **The header is relocated, never dropped.** Attribution's contract (the downloader still sees whose
   code each section is, and under what terms) holds — the license simply sits below the parameters
   instead of above them. Under `--minify`/`--obfuscate` the header is still sticky and survives unless
   `--strip-license`.

3. **Survives tree-shaking.** Moving the header onto the first *body* statement exposed a latent
   fragility shared with the synthesized `/* [Hidden] */` fence: a body statement can be removed by
   dead-code elimination, which would silently drop any sticky trivia riding on it (the license, the
   fence). `DeadCodeElimination` now **carries sticky leading trivia forward** onto the next surviving
   statement when it drops a node, so the license and the Customizer fence both survive a hardened
   `--parameters-first` bundle.

4. **Scope: the aggregated header only.** The flag relocates the `--bundle-licenses` output (the
   default, and the scenario that triggers the bug). With `--no-bundle-licenses` there is no aggregated
   block above the parameters to move, so the flag is a documented no-op in that mode. It is likewise a
   no-op when the root declares no Customizer parameters (nothing to hoist above), or when nothing
   carries a header.

5. **Layout matches the proven manual fix:** `parameters → license header → /* [Hidden] */ → body`.
   The Customizer-visible region is exactly the parameters; the license follows them, ahead of the
   Hidden fence. The fence still hides every body global from the Customizer.

## Alternatives considered

- **Make it the default.** Rejected: the default should reflect ScadBundler's own intent (attribution
  at the top — credits lead), and most platforms read the parameters correctly regardless of preceding
  comments. Changing the default to satisfy one platform's bug would degrade the common case and the
  social signal that the credits come first.

- **Place the header *after* the `/* [Hidden] */` fence (inside the Hidden group).** Cleaner in theory
  (the Customizer-visible region would be strictly the parameters) but it diverges from the layout the
  manual fix proved works on Thingiverse, and offers no observed benefit. Kept it before the fence to
  match the validated arrangement.

- **Force the header onto a guaranteed-protected node (e.g. keep it as trailing trivia of the last
  parameter).** Rejected: trailing trivia emits inline on the parameter's line, which would render the
  first header line as if it were the parameter's annotation — visually wrong and a Customizer hazard.
  Carrying sticky trivia forward in DCE is the principled fix and also hardens the pre-existing fence.

- **Couple the flag name to the platform (`--thingiverse-compat`).** Rejected: vendor-named flags age
  badly and over-promise. The behavior is described generically (`--parameters-first`) and the
  Thingiverse rationale lives in the docs.

## Consequences

- A new opt-in flag, off by default; existing bundles are unchanged.
- The license/attribution still appears in every `--parameters-first` bundle (just below the
  parameters), so attribution is preserved — addressing the concern that hardening/relocation might be
  read as stripping credit.
- DCE now preserves sticky leading trivia across statement removal, fixing a latent case where the
  `/* [Hidden] */` fence (or, now, a relocated license) could be dropped with its host statement.
- Casual viewers of a `--parameters-first` bundle see the Customizer knobs first and the attribution
  second; documented as an accepted trade-off of the workaround.

## Implementation pointers

- Flag plumbing: [BundleOptions.cs](../../src/ScadBundler.Core/Inlining/BundleOptions.cs)
  (`ParametersFirst`), [BundleCommand.cs](../../src/ScadBundler/BundleCommand.cs) (`--parameters-first`
  + usage), and the web facade ([WebBundleOptions.cs](../../src/ScadBundler.Core/Workspace/WebBundleOptions.cs),
  [WebBundler.cs](../../src/ScadBundler.Core/Workspace/WebBundler.cs), `OptionsPanel.razor`).
- Relocation: [Inliner.cs](../../src/ScadBundler.Core/Inlining/Inliner.cs) `Assemble` — the header is
  anchored to the first post-prologue (body) statement instead of the first parameter.
- Trivia survival: [DeadCodeElimination.cs](../../src/ScadBundler.Core/Transforming/DeadCodeElimination.cs)
  carries sticky leading trivia from a dropped statement onto the next kept one.
- Tests: `Slice5BundleTests`/`AttributionTests` (ordering), `TransformTests` (sticky-trivia survival
  under minify), `CliTests` (`--parameters-first` end-to-end), `BundleParityTests` (web parity).
