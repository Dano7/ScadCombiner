# Distribution & Packaging Plan

> Status: **Proposed — for review.** No GitHub configuration has been applied yet.
> This document is the design/plan only; implementation (Stage 2) happens after sign-off.

## Context

Users report the **ScadBundler Live** web app is too slow for large `.scad` codebases
(BOSL2 / NopSCADlib scale). The cause is structural, not a bug: the web app is
single-threaded Blazor WebAssembly with **no access to the local filesystem** — every
file must be hand-uploaded, and the bundle pass blocks the UI thread. See
[`WorkspaceController`](../web/ScadBundler.Web/State/WorkspaceController.cs), which already
falls back to a manual "Bundle" button and shows a "Large project" notice past 12 files / 256 KB.

What users actually want is a **fast local tool that reads their existing OpenSCAD library
layout**. The good news: the CLI already does this. It resolves `OPENSCADPATH` and the
per-user OpenSCAD library folder today via
[`OpenScadEnvironment.LibraryPaths()`](../src/ScadBundler.Core/Loading/OpenScadEnvironment.cs).
A native build (CLI now, GUI later) runs on full .NET with real threads and direct disk
access, so it is fast **and** uses the libraries users have already organized — no upload step.

The blocker is **distribution**, not capability:

- The tool is packaged as a `dotnet tool` but has **never been published** anywhere, and the
  target audience (OpenSCAD makers, Thingiverse / MakerWorld / Printables uploaders) mostly
  **does not have the .NET SDK** and should not need to learn `dotnet tool`.
- There is **no release automation** — CI builds, tests, and runs `dotnet pack` to an artifact,
  but nothing is published, there are no GitHub Releases, and the version is hardcoded `0.1.0`.
- We need **portable executables, a Windows installer story, and trust**, without losing
  cross-platform support or creating installers that are painful to maintain as the project evolves.

### Goals

1. Ship **Windows options soon**: a portable single `.exe`, a zip-on-your-PATH, winget, and the Microsoft Store.
2. Keep **full cross-platform** coverage (every OS OpenSCAD supports: Windows, macOS, Linux — x64 + arm64).
3. **Fully automated and GitHub-native** — one git tag drives every artifact and channel.
4. **Low maintenance** — the release pipeline must not grow more fragile as the project evolves.
5. **Keep deployment/cross-platform concerns out of the core.** `ScadBundler.Core` stays
   dependency-free and untouched; all packaging lives at the edges.

### Decisions taken at review (2026-06-15)

| Question | Decision |
| --- | --- |
| GUI scope | **CLI now on every platform; a Blazor *desktop* app later** that reuses the existing web UI |
| Windows channels | **winget + portable zip/exe + Microsoft Store (MSIX)** |
| Code signing | **Unsigned to start, but the pipeline is built signing-ready** (flip on later, no rework) |

---

## Architecture: where deployment lives (and where it doesn't)

The repo already has the right seams. This plan touches **only the edges**:

```
ScadBundler.Core      ← UNCHANGED. Zero deps, no reflection, IFileSystem seam. No packaging code, ever.
  │
  ├─ src/ScadBundler            (CLI)        ← gains publish PROFILES + package metadata only (no logic)
  ├─ web/ScadBundler.Web        (Blazor WASM)← unchanged; keeps auto-deploying to GitHub Pages
  └─ desktop/ScadBundler.Desktop (Phase 2)   ← NEW: native shell reusing the web UI

packaging/            ← NEW: MSIX manifest, icons, winget templates, signing config (kept out of src/)
.github/workflows/
  ├─ ci.yml           ← unchanged (PR build/test/pack)
  ├─ deploy-pages.yml ← unchanged (web)
  └─ release.yml      ← NEW: tag-driven; builds & publishes every artifact/channel
```

Concretely, to keep the core clean:

- **Publish knobs go in profiles, not the csproj body.** Per-RID `Properties/PublishProfiles/*.pubxml`
  files in the CLI project hold AOT/single-file/trim settings, so the csproj stays readable and
  normal `dotnet build`/`dotnet test` are unaffected.
- **All packaging assets** (`Package.appxmanifest`, Store/winget manifests, icons, signing scripts)
  live in a top-level `packaging/` folder, not under `src/`.
- **All release automation** lives in `release.yml`, separate from CI and from the web deploy.

---

## Distribution channels

One **GitHub Release per tag** is the hub; every channel is fed from it automatically.

| Channel | Platforms | Audience | Install command / action | Cost / account | Maintenance |
| --- | --- | --- | --- | --- | --- |
| **Portable single-exe + zip** | win, mac, linux (x64+arm64) | Anyone; makers who "just want the exe" | Download from Releases, unzip, run / add to PATH | none | trivial (matrix build) |
| **winget** | Windows | Windows users | `winget install ScadBundler` | none | auto-bumped from Release |
| **Microsoft Store (MSIX)** | Windows | Non-technical, "obtain & trust" | Search Store → Get | ~$19 one-time (Partner Center) | submission automated |
| **NuGet `dotnet tool`** | any with .NET | Developers who already have .NET | `dotnet tool install -g ScadBundler` | free NuGet account | auto-pushed on tag |
| **Homebrew tap** *(recommended for mac/linux, optional)* | mac, linux | mac/linux CLI users | `brew install dano7/tap/scadbundler` | none (a small tap repo) | auto-bumped from Release |
| **Chocolatey** *(optional, deferred)* | Windows | Dev/IT | `choco install scadbundler` | none | per-release moderation latency |

The portable exe, winget, NuGet, and Homebrew are **all zero-cost and fully automated** — these
deliver the "Windows + cross-platform soon" goal. The Store is the one channel needing a one-time
paid account; it pairs naturally with the Phase 2 GUI but can ship a CLI-MSIX sooner if desired.

---

## The portable executable: technology choice

Target: a **true single, self-contained `.exe`** that needs **no .NET install** — exactly what users asked for.

**Primary: Native AOT.** The core is an ideal AOT candidate — zero NuGet dependencies, no
reflection, no `System.Text.Json`, hand-written lexer/parser, hand-rolled CLI arg parsing. Native AOT
yields a single native binary (~3–10 MB), instant startup, and no runtime dependency. We enforce
AOT-safety so it stays low-maintenance: set `<IsAotCompatible>true</IsAotCompatible>` and
`<InvariantGlobalization>true</InvariantGlobalization>`; the repo already has `TreatWarningsAsErrors`,
so any future code that breaks AOT/trim fails the build loudly instead of silently bloating the binary.

**Guaranteed fallback: self-contained single-file + trimming.** If any single RID hits an AOT
snag, that RID falls back to `--self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true`
(~15–30 MB, still no .NET install needed) without blocking the release.

**Build matrix** (Native AOT must build on its target OS — the matrix handles this):

| RID | Runner |
| --- | --- |
| `win-x64`, `win-arm64` | `windows-latest` (arm64 cross-compiled with ARM64 build tools) |
| `osx-x64`, `osx-arm64` | `macos-latest` |
| `linux-x64` | `ubuntu-latest` |
| `linux-arm64` | `ubuntu-24.04-arm` |

Each job emits `scadbundler-<rid>.zip` (+ a bare `.exe`/binary for Windows) and a SHA256 checksum,
attached to the GitHub Release.

---

## Versioning & release trigger

- **Adopt [MinVer](https://github.com/adamralph/minver)** (a tiny build-time dependency on the
  *CLI/packaging projects only*, never on Core): the version is derived from the git tag. No more
  hardcoded `0.1.0` to forget to bump.
- **One trigger:** push an annotated tag `vX.Y.Z` → `release.yml` builds every artifact, publishes
  every channel, and cuts the GitHub Release with auto-generated notes. `workflow_dispatch` is also
  wired for manual/dry runs.
- Add a `CHANGELOG.md` (keep-a-changelog) for human-readable notes; GitHub auto-notes cover the rest.

This is the core of the "low maintenance" promise: **a new release is one `git tag` + `git push --tags`.**

---

## Phase plan

### Phase 0 — Foundations (small, no behavior change)
- Add package metadata to [`ScadBundler.csproj`](../src/ScadBundler/ScadBundler.csproj):
  `RepositoryUrl`, `PackageProjectUrl`, `PackageReadmeFile`, `PackageTags`, real `Authors`,
  `PackageIcon` (optional). License/`PackAsTool`/`ToolCommandName` already present.
- Add per-RID publish profiles; add `IsAotCompatible` + `InvariantGlobalization`.
- Add MinVer; add `global.json` pinning the SDK major with `rollForward: latestFeature` (reproducible, still low-touch).
- Scaffold `packaging/` and `CHANGELOG.md`.

### Phase 1 — Automated cross-platform releases  *(delivers Windows + all platforms "soon", zero cost)*
- `release.yml`: the AOT matrix above → portable zips + single exes + checksums → **GitHub Release**.
- **Publish the `dotnet tool` to NuGet.org** (see maintainer steps below) via `dotnet nuget push`.
- **winget**: auto-submit/refresh the manifest from each Release (e.g. `vedantmgoyal2009/winget-releaser`).
- *(Recommended)* **Homebrew tap** repo `dano7/homebrew-tap`, formula auto-bumped from the Release.

### Phase 1b — Microsoft Store (MSIX)  *(needs the Partner Center account)*
- One-time: create a Partner Center account (~$19), **reserve the "ScadBundler" name**.
- Build an MSIX in `release.yml` from `packaging/Package.appxmanifest`. For a CLI-in-Store, declare a
  `windows.appExecutionAlias` so `scadbundler` works from any terminal after install.
- Automate submission via the `microsoft/store-submission` GitHub Action (Azure AD app + Partner
  Center association). **The Store signs the package for you** — this is why "unsigned now" is
  compatible with shipping to the Store: Store trust is independent of our own signing cert.

### Phase 2 — Blazor desktop GUI  *(fast local GUI; fixes the original complaint)*
- **Extract the shared UI** from [`web/ScadBundler.Web`](../web/ScadBundler.Web) into a Razor Class
  Library `ScadBundler.UI`. Both the WASM web app and the new desktop app consume the same components —
  one UI, two hosts.
- **`desktop/ScadBundler.Desktop`** built on **Photino.Blazor** (cross-platform Win/Mac/Linux, MIT,
  lightweight OS-WebView). *Not MAUI* — MAUI Blazor Hybrid has no Linux desktop target, which would
  break the cross-platform requirement.
- The desktop host swaps `InMemoryFileSystem` → [`DiskFileSystem`](../src/ScadBundler.Core/Loading/IFileSystem.cs)
  + a native folder picker, and reads `OPENSCADPATH`. Result: the same familiar UI as the web app, but
  running on **full .NET with direct disk access** → fast on BOSL2-scale projects and using the user's
  existing library paths. This is the natural product for the Microsoft Store.

### Phase 3 — Trust & extra channels (later, hooks built in earlier)
- **Azure Trusted Signing** for Windows (~$10/mo, GitHub-Actions-native) → removes SmartScreen warnings on the portable exe/winget.
- **Apple notarization** (Apple Developer, $99/yr) → removes Gatekeeper warnings on macOS.
- Optional **Chocolatey**, Linux `.deb`/AppImage/Flatpak. All slot into `release.yml` without rework.

---

## Publishing as a `dotnet tool` (maintainer instructions)

**One-time setup**
1. Create a free account at **nuget.org** (sign in with Microsoft or GitHub).
2. Confirm the package id **`ScadBundler`** is available; if taken, fall back to e.g. `OpenScadBundler`
   — note the *command users type*, `ToolCommandName=scadbundler`, is independent of the package id.
3. Create an **API key** (Account → API Keys): scope **Push**, glob pattern `ScadBundler`, ~1-year expiry.
4. Add it to the GitHub repo as an Actions secret named **`NUGET_API_KEY`**.
5. Fill in the package metadata (Phase 0).

**Each release (automated in `release.yml`)**
```bash
dotnet pack src/ScadBundler -c Release -o artifacts        # version comes from the git tag via MinVer
dotnet nuget push artifacts/*.nupkg \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json --skip-duplicate
```
The package appears on nuget.org within a few minutes of the tag push.

## How people install & use it

**Recommended for OpenSCAD makers (no .NET required):**
- **Windows:** `winget install ScadBundler`, or grab `scadbundler-win-x64.zip` from Releases and run
  the exe / add it to PATH, or get it from the **Microsoft Store**.
- **macOS / Linux:** `brew install dano7/tap/scadbundler`, or download the portable tarball for your arch.

**For developers who already have .NET:**
```bash
dotnet tool install --global ScadBundler        # install
scadbundler bundle myproject.scad -o bundled.scad
dotnet tool update  --global ScadBundler        # update
dotnet tool uninstall --global ScadBundler      # remove
```
All paths support the same CLI ([`docs/UX.md`](UX.md)) and read `OPENSCADPATH` + the per-user OpenSCAD
library folder automatically, so existing library layouts work with no extra flags.

---

## Secrets & accounts checklist

| Item | Needed for | When |
| --- | --- | --- |
| `NUGET_API_KEY` (repo secret) | `dotnet tool` publish | Phase 1 |
| Partner Center account (~$19) + reserved name | Microsoft Store | Phase 1b |
| Azure AD app + Partner Center association (repo secrets) | automated Store submission | Phase 1b |
| `dano7/homebrew-tap` repo + a PAT | Homebrew auto-bump | Phase 1 (optional) |
| Azure Trusted Signing account (~$10/mo) | Windows signing | Phase 3 |
| Apple Developer ($99/yr) | macOS notarization | Phase 3 |

GitHub's built-in `GITHUB_TOKEN` covers creating Releases and uploading assets — no secret needed for that.

## One-time checks before publishing
- Verify the `ScadBundler` package id is free on NuGet (and the Store/winget name).
- Provide an app **icon** + short Store description/screenshots (reuse web app branding).
- Decide the first public version tag (suggest `v0.2.0`, since `0.1.0` was never released).

---

## What "Stage 2" (after approval) sets up on GitHub

1. Phase 0 csproj/profile/metadata edits + `global.json` + MinVer + `packaging/` scaffold + `CHANGELOG.md`.
2. `.github/workflows/release.yml` (matrix build → GitHub Release → NuGet push → winget submit [→ Homebrew]).
3. Repo secret wiring instructions for `NUGET_API_KEY` (you add the secret value; I wire the workflow).
4. Microsoft Store MSIX packaging + submission workflow (gated on your Partner Center account).
5. Phase 2 desktop scaffolding (`ScadBundler.UI` RCL + `desktop/ScadBundler.Desktop`) as a follow-on.

Existing `ci.yml`, `deploy-pages.yml`, and `codeql.yml` stay as-is.
