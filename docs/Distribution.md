# Distribution & Packaging Plan

> This describes how ScadBundler is built and delivered to users. The **portable binaries,
> automated cross-platform releases, and `dotnet tool` publishing are implemented**
> ([`release.yml`](../.github/workflows/release.yml)); MSIX/Store is scaffolded; a native GUI is
> backlog. Maintainer steps live in [Releasing.md](Releasing.md); end-user steps in [Install.md](Install.md).

## Context

Users report the **ScadBundler Live** web app is too slow for large `.scad` codebases
(BOSL2 / NopSCADlib scale). The cause is structural: the web app is single-threaded Blazor
WebAssembly with **no local filesystem access** — every file is hand-uploaded and the bundle pass
blocks the UI thread. What users want is a **fast local tool that reads their existing OpenSCAD
library layout**. The engine already resolves `OPENSCADPATH` + the per-user OpenSCAD library folder
([`OpenScadEnvironment.LibraryPaths()`](../src/ScadBundler.Core/Loading/OpenScadEnvironment.cs)); it
just needs to reach users as a native download.

The blocker was **distribution**, not capability: the CLI was packaged as a `dotnet tool` but never
published, the target audience (OpenSCAD makers, Thingiverse / MakerWorld / Printables uploaders)
mostly lacks the .NET SDK, and there was no release automation.

### Goals

1. Ship **portable Windows executables soon** (single `.exe` + a zip-on-your-PATH), plus winget and an MSIX/Store path.
2. Keep **full cross-platform** coverage (Windows, macOS, Linux — x64 + arm64).
3. **Fully automated and GitHub-native** — one git tag drives every artifact and channel.
4. **Low maintenance**; **keep deployment concerns out of the core** — `ScadBundler.Core` stays
   dependency-free and untouched; all packaging lives at the edges.

### Decisions (reviewed 2026-06-15)

| Topic | Decision |
| --- | --- |
| Priority | **Portable Windows exes + automated cross-platform releases + MSIX**, in that order. |
| GUI roadmap | The **web Blazor app fills the GUI need today**; a Blazor *desktop* app is a possible later step. A **native Windows GUI (WinUI 3) is on the backlog** for a polished, Store-grade experience. |
| Windows channels | winget + portable zip/exe + Microsoft Store (MSIX). |
| Code signing | **Unsigned.** Paid signing (Azure Trusted Signing ~$10/mo; Apple notarization ~$99/yr) is **deferred until sponsor-funded**; [Install.md](Install.md) explains the one-time "Run anyway" step and carries a sponsorship appeal. |

---

## Architecture: where deployment lives (and where it doesn't)

The plan touches **only the edges**:

```
ScadBundler.Core      ← UNCHANGED. Zero deps, no reflection, IFileSystem seam. No packaging code, ever.
  ├─ src/ScadBundler            (CLI)        ← gains package metadata + a guarded PortablePublish group
  └─ web/ScadBundler.Web        (Blazor WASM)← unchanged; keeps auto-deploying to GitHub Pages

packaging/            ← MSIX manifest, placeholder icons, build script (kept out of src/)
.github/workflows/
  ├─ ci.yml           ← unchanged (PR build/test/pack)
  ├─ deploy-pages.yml ← unchanged (web)
  └─ release.yml      ← NEW: tag-driven; builds & publishes every artifact/channel
```

To keep the core clean, the only CLI-project change with runtime effect is `InvariantGlobalization`
(the bundler is culture-invariant by design). Portable publish settings live in a
`Condition="'$(PortablePublish)'=='true'"` group that's inert for normal build/test/pack. All
release automation is in `release.yml`, separate from CI and the web deploy.

---

## Phase plan

### Phase 1 — Automated cross-platform releases  *(IMPLEMENTED in this PR)*

- **Foundations:** package metadata + `PackageReadmeFile` on
  [`ScadBundler.csproj`](../src/ScadBundler/ScadBundler.csproj); [MinVer](https://github.com/adamralph/minver)
  (version from the git tag — the hardcoded `0.1.0` is gone); `global.json` (SDK pin); `LICENSE`;
  `CHANGELOG.md`.
- **`release.yml`** (tag `vX.Y.Z` or a `workflow_dispatch` dry run): publishes portable,
  self-contained, single-file, trimmed binaries for all six RIDs from a single Windows runner (which
  also builds the MSIX — `makeappx` is Windows-only), zips + checksums them, packs the `dotnet tool`,
  creates the **GitHub Release**, pushes to **NuGet**
  (via **Trusted Publishing**, gated on the `NUGET_USER` variable), and submits the **winget** manifest (gated on `WINGET_TOKEN`).
- **Docs:** [Install.md](Install.md) (per-platform, SmartScreen + sponsorship), [Releasing.md](Releasing.md);
  `.github/FUNDING.yml`.

### Phase 2 — Microsoft Store (MSIX)  *(scaffolded here; needs the Partner Center account)*

`packaging/msix/` ships the manifest, placeholder logos, and `build-msix.ps1`; `release.yml` builds
an unsigned MSIX as a preview (best-effort, non-blocking). The **Store signs the package for you** —
which is why "unsigned now" is compatible with shipping to the Store. Wiring the
`microsoft/store-submission` action is left dormant until the ~$19 Partner Center account exists.

### Backlog / Future

- **Native Windows GUI (WinUI 3 / Windows App SDK)** — a fully native front-end (Fluent design, deep
  OS/Store integration, no WebView) for a polished, Store-grade Windows app. Windows-only; shares
  `ScadBundler.Core`, separate UI. Until then the **Blazor web app covers the GUI need**, and a
  cross-platform Blazor desktop shell (Photino) remains an option.
- **Sponsor-funded code signing** — Azure Trusted Signing (Windows) / Apple notarization (macOS).
  The pipeline is signing-ready; either flips on with no rework once funded.
- **Optional channels** — Homebrew tap (mac/linux), Chocolatey (Windows), Linux `.deb`/AppImage/Flatpak.

---

## Distribution channels

One **GitHub Release per tag** is the hub; every channel is fed from it.

| Channel | Platforms | Audience | Install | Cost | Status |
| --- | --- | --- | --- | --- | --- |
| **Portable zip + single exe** | win/mac/linux (x64+arm64) | "just give me the exe" | download, run / add to PATH | none | ✅ this PR |
| **winget** | Windows | Windows users | `winget install ScadBundler` | none | ✅ gated on token |
| **NuGet `dotnet tool`** | any with .NET | developers | `dotnet tool install -g ScadBundler` | free account | ✅ gated on key |
| **Microsoft Store (MSIX)** | Windows | non-technical "obtain & trust" | Store → Get | ~$19 one-time | 🟡 scaffolded |
| **Homebrew tap** | mac/linux | mac/linux CLI users | `brew install …` | none | ⬜ backlog |

---

## Portable executable: technology choice

Target: artifacts that need **no .NET install**.

- **Shipping default — self-contained, single-file, trimmed.** Verified: an 11 MB
  `scadbundler.exe` that runs a real multi-file bundle correctly. It **cross-compiles** — a single
  Windows runner emits all six platforms (and builds the MSIX) — far lower maintenance than a per-OS
  toolchain matrix.
  `InvariantGlobalization` drops ICU; trimming is safe here because the engine uses no reflection.
- **Future upgrade — Native AOT.** Smaller/faster, and the engine is AOT-clean, but it needs per-OS
  native toolchains (MSVC/clang/Xcode) validated on each runner (it does **not** cross-compile). It's
  a one-flag switch (`-p:PublishAot=true`) once validated in CI — deliberately deferred so the first
  release ships on the proven path.

## Versioning & release trigger

**MinVer** derives the version from the git tag; a release is **`git tag vX.Y.Z && git push --tags`**
(or a `workflow_dispatch` dry run). That is the whole maintenance story. See [Releasing.md](Releasing.md).

## Publishing as a `dotnet tool`

Maintainer steps (NuGet account, a Trusted-Publishing policy + `NUGET_USER` variable, pack/push —
automated in `release.yml`) and the end-user `dotnet tool install --global ScadBundler` flow are documented in
[Releasing.md](Releasing.md) and [Install.md](Install.md) respectively.

## Secrets & accounts

| Item | For | When |
| --- | --- | --- |
| NuGet Trusted Publishing (`NUGET_USER` variable + nuget.org policy) | `dotnet tool` publish | Phase 1 (OIDC; no API-key secret) |
| `WINGET_TOKEN` | winget PRs | Phase 1 (optional) |
| GitHub Sponsors | the signing appeal | Phase 1 (enable Sponsors; `FUNDING.yml` is in place) |
| Partner Center (~$19) + Azure AD | Microsoft Store | Phase 2 |
| Azure Trusted Signing / Apple Developer | signing | deferred — sponsor-funded |

`GITHUB_TOKEN` (built in) covers creating Releases and uploading assets — no secret needed.
