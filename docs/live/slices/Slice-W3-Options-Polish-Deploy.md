# Slice W3 — Options, Polish & Deploy

**Status**: spec ready (not started).
**Project**: `web/ScadBundler.Web/` (+ a CI/deploy workflow).
**Depends on**: [Slice-W2](Slice-W2-Dependency-UX.md).
**Read with**: [../Spec.md](../Spec.md) §3.5 (options↔CLI mapping), [../Design.md](../Design.md) §5 (hosting).

The shipping slice: expose the bundler's flags as friendly controls, make the page responsive and
accessible, handle empty/error states gracefully, and publish it to a static host.

---

## 1. Goal

Give users the size/credit controls from the vision and ship the page. Every option maps to an existing
CLI flag (no new behavior), re-bundles live, and the published static build loads and works from a host.

**Non-goals (this slice):** the 3D preview / Customizer (W4); a backend; new diagnostics.

---

## 2. Deliverables / behavior

### 2.1 Options panel (`OptionsPanel`, collapsed by default)

Maps to `WebBundleOptions` (Spec §3.5 / §5.4). Visible:

- ☐ **Remove provenance banners / license aggregation** → `BundleLicenses = false`. **Default off**
  (keep attribution). A short note explains it bundles each library's license + a provenance banner.
- ◉ **Normal** / ○ **Minify** / ○ **Obfuscate** radio → `Hardening`. Mutually exclusive by construction.
  A **hover tooltip / expandable note** carries the vision's message verbatim in spirit: *"This isn't
  about denying credit — the license stays. Minify/obfuscate just nudges the curious to learn from the
  original sources listed at the top, not from this flattened copy."*

**Advanced** sub-section (collapsed):

- **Collision strategy** select → `OnCollision` (auto / prefix / error / keep-first / keep-last); Auto
  default, with one-line descriptions.
- ☐ **Strip license under minify/obfuscate** → `StripLicense`; only enabled when a hardening profile is
  selected.
- ☐ **Keep comments** (default on) → `PreserveComments`; disabled/ignored under Minify.

Changing any control calls `WorkspaceController.SetOptions` → re-bundle live → `OutputPanel` + stats
update. The mapping must produce the **same bytes** as the equivalent CLI invocation (W0 parity guarantee).

### 2.2 Polish

- **Responsive** layout (desktop → mobile single column); the drop zone, file list, and output reflow.
- **Accessibility**: keyboard-operable drop zone (a real `<input type=file>` fallback), focus states,
  ARIA on status icons and the problems list, sufficient contrast, labelled controls.
- **States**: first-load (engine loading), empty (no files), incomplete (needs N files), error
  (Error-severity diagnostics → explain, keep output disabled), success.
- **Stats line**: files combined · output size · (renamed/removed counts when a profile ran).

### 2.3 Deploy

- `dotnet publish web/ScadBundler.Web -c Release` → static `wwwroot/` with **trimming** + **Brotli** +
  invariant globalization (Design §5). Verify payload size and first-interaction time.
- A **CI workflow** publishing on push (host chosen with the owner — GitHub Pages / Cloudflare / Azure
  SWA): base-href for sub-path hosting, SPA fallback to `index.html`, correct `.wasm`/`.br` MIME +
  long-cache headers.
- Update the docs landing [README.md](../../README.md) companion link (already points at this `live/`
  package — swap in the live URL once deployed) + a one-line "or use the web version" pointer.

---

## 3. Scope (In / Out)

**In:** the options panel + live re-bundle + CLI-parity mapping; responsive/a11y/state polish; the
publish settings + CI/deploy + README link.

**Out:** preview/Customizer (W4); worker-thread bundling (perf stretch); Monaco.

---

## 4. Test plan

- **bUnit**: each control updates `WebBundleOptions` correctly; the radio enforces mutual exclusion;
  "Strip license" enables only under a profile; toggling an option triggers a re-bundle.
- **Parity**: for each option combination, assert the produced text equals the matching CLI flags
  (extend the W0 parity fixtures with option permutations).
- **Manual**: minify shrinks the file and keeps the license header; obfuscate keeps the license + drops
  banners; "remove provenance" drops the banners; a11y pass (keyboard-only run-through, screen-reader
  labels); load the **published** build from the chosen host and bundle a real project.

---

## 5. Exit criteria

- [ ] Every option re-bundles live and produces output byte-identical to the equivalent CLI flag.
- [ ] Minify/obfuscate keep the aggregated license by default; "strip license" opts out; "remove
      provenance banners" drops the banners — all matching CLI semantics.
- [ ] Responsive + accessible (keyboard-operable, labelled, sufficient contrast); empty/incomplete/error
      states are clear.
- [ ] `dotnet publish` yields a trimmed, Brotli-compressed static site; CI publishes it; it loads and
      bundles from the chosen static host.
- [ ] Root `README.md` companion link points to the live site.
