# Distribution & Packaging Plan

> Status: **Proposed — for review.** No GitHub configuration has been applied yet.
> This document is the design/plan only; implementation happens after sign-off.

## Context

Users report the **ScadBundler Live** web app is too slow for large `.scad` codebases
(BOSL2 / NopSCADlib scale). The cause is structural, not a bug: the web app is
single-threaded Blazor WebAssembly with **no access to the local filesystem** — every
file must be hand-uploaded, and the bundle pass blocks the UI thread. See
[`WorkspaceController`](../web/ScadBundler.Web/State/WorkspaceController.cs), which already
falls back to a manual "Bundle" button and shows a "Large project" notice past 12 files / 256 KB.

What users actually want is a **fast local tool that reads their existing OpenSCAD library
layout**. The good news: the engine already does this. It resolves `OPENSCADPATH` and the
per-user OpenSCAD library folder today via
[`OpenScadEnvironment.LibraryPaths()`](../src/ScadBundler.Core/Loading/OpenScadEnvironment.cs).
A native build runs on full .NET with real threads and direct disk access, so it is fast **and**
uses the libraries users have already organized — no upload step.

The blocker is **distribution**, not capability:

- The CLI is packaged as a `dotnet tool` but has **never been published** anywhere, and the
  target audience (OpenSCAD makers, Thingiverse / MakerWorld / Printables uploaders) mostly
  **does not have the .NET SDK** and should not need to learn `dotnet tool`.
- There is **no release automation** — CI builds, tests, and runs `dotnet pack` to an artifact,
  but nothing is published, there are no GitHub Releases, and the version is hardcoded `0.1.0`.
- We need **a fast local GUI, portable executables, a Windows installer story, and trust**, without
  losing cross-platform support or creating installers that are painful to maintain.

### Goals

1. Deliver a **fast local GUI** that reuses the existing web UI but reads local library paths.
2. Ship **Windows options soon**: portable single `.exe`, a zip-on-your-PATH, winget, Microsoft Store.
3. Keep **full cross-platform** coverage (every OS OpenSCAD supports: Windows, macOS, Linux — x64 + arm64).
4. **Fully automated and GitHub-native** — one git tag drives every artifact and channel.
5. **Low maintenance**; **keep deployment/cross-platform concerns out of the core** (`ScadBundler.Core`
   stays dependency-free and untouched; all packaging lives at the edges).

### Decisions (reviewed 2026-06-15)

| Question | Decision |
| --- | --- |
| What we build **first** | The **Blazor *desktop* app** — reuse the existing web UI, run it natively (fast, local disk, reads `OPENSCADPATH`). |
| GUI roadmap | Cross-platform Blazor desktop now; **a native Windows GUI (WinUI 3) is on the backlog** for a more polished, Store-grade Windows experience later. |
| Windows channels | **winget + portable zip/exe + Microsoft Store (MSIX)** |
| Code signing | **Unsigned.** Paid signing (Azure Trusted Signing ~$10/mo; Apple notarization ~$99/yr) is **deferred until sponsor-funded** — the install docs explain the one-time "Run anyway" step and carry a **sponsorship appeal** to fund it. |

---

## Architecture: where deployment lives (and where it doesn't)

The repo already has the right seams. This plan touches **only the edges**:

```
ScadBundler.Core      ← UNCHANGED. Zero deps, no reflection, IFileSystem seam. No packaging code, ever.
  │
  ├─ src/ScadBundler            (CLI)        ← gains publish PROFILES + package metadata only (no logic)
  ├─ ScadBundler.UI             (NEW RCL)    ← shared Razor components/state, extracted from the web app
  ├─ web/ScadBundler.Web        (Blazor WASM)← keeps auto-deploying to Pages; now consumes the shared UI
  └─ desktop/ScadBundler.Desktop (NEW)       ← native Photino host: same UI, DiskFileSystem, OPENSCADPATH

packaging/            ← NEW: MSIX manifest, icons, winget templates, signing config (kept out of src/)
.github/workflows/
  ├─ ci.yml           ← unchanged (PR build/test/pack)
  ├─ deploy-pages.yml ← unchanged (web)
  └─ release.yml      ← NEW: tag-driven; builds & publishes every artifact/channel
```

To keep the core clean: publish knobs live in per-RID `Properties/PublishProfiles/*.pubxml`
profiles (not the csproj body); all packaging assets live in a top-level `packaging/` folder; all
release automation lives in `release.yml`, separate from CI and the web deploy.

---

## Phase plan

### Phase 1 — Blazor desktop app  *(NOW — the fast local GUI that fixes the complaint)*

Reuse the existing web UI, host it natively so it runs on full .NET with direct disk access.

1. **Extract the shared UI into a Razor Class Library `ScadBundler.UI`** — move the Razor components
   (`Landing`, `DropZone`, `StructureTree`, `FileList`, `LargeProjectNotice`, options panel, …),
   [`State/WorkspaceController`](../web/ScadBundler.Web/State/WorkspaceController.cs), and the
   `Ingestion/` code out of [`web/ScadBundler.Web`](../web/ScadBundler.Web). Both the WASM web app
   and the desktop app reference it — **one UI, two hosts.**
2. **Abstract host services** behind a small interface (pick files/folder, read, write/download output,
   clipboard). Web implements it via the existing `interop.js`; desktop implements it via native dialogs.
3. **`desktop/ScadBundler.Desktop` on [Photino.Blazor](https://github.com/tryphotino/photino.Blazor)**
   (cross-platform Win/Mac/Linux, MIT, lightweight OS-WebView). *Not MAUI* — MAUI Blazor Hybrid has no
   Linux desktop target, which would break cross-platform.
4. **Wire real disk access**: the desktop host registers
   [`DiskFileSystem`](../src/ScadBundler.Core/Loading/IFileSystem.cs) and calls
   `OpenScadEnvironment.LibraryPaths()`, so the app bundles directly from folders on disk, on full
   .NET (fast on BOSL2-scale projects), resolving the user's existing `OPENSCADPATH` + per-user library
   folder — the key advantage over the browser.
5. **Run/verify**: `dotnet run --project desktop/ScadBundler.Desktop`; shared components keep their
   bUnit coverage. Distribution of this app is Phase 2.

> Runtime note: Photino uses the OS WebView (WebView2 on Windows — present on Win 11, auto-installed
> otherwise; WebKitGTK on Linux; WKWebView on macOS). These are declared as dependencies in packaging.

### Phase 2 — Automated cross-platform releases  *(packages the desktop app + CLI; zero cost)*

- **Foundations:** add package metadata to [`ScadBundler.csproj`](../src/ScadBundler/ScadBundler.csproj)
  (`RepositoryUrl`, `PackageProjectUrl`, `PackageReadmeFile`, `PackageTags`, real `Authors`, icon);
  add per-RID publish profiles; add [MinVer](https://github.com/adamralph/minver) (version from the git
  tag — no more hardcoded `0.1.0`); add `global.json` (pin SDK major, `rollForward: latestFeature`);
  scaffold `packaging/` and `CHANGELOG.md`.
- **`release.yml`** (tag `vX.Y.Z` → everything): a per-OS build matrix produces portable artifacts for
  the **desktop app** and the **CLI**, plus checksums, into a **GitHub Release**.
- **NuGet `dotnet tool`** publish (CLI) via `dotnet nuget push` (see maintainer steps below).
- **winget** auto-submit/refresh from each Release (e.g. `vedantmgoyal2009/winget-releaser`).
- *(Recommended)* **Homebrew tap** `dano7/homebrew-tap`, formula auto-bumped — the mac/linux equivalent of winget.

### Phase 3 — Microsoft Store (MSIX)  *(the desktop GUI, needs the Partner Center account)*

- One-time: Partner Center account (~$19), **reserve the "ScadBundler" name**.
- Build an MSIX of the desktop app from `packaging/Package.appxmanifest` in `release.yml`; automate
  submission via the `microsoft/store-submission` action (Azure AD app + Partner Center association).
- **The Store signs the package for you** — which is why "unsigned now" is compatible with shipping to
  the Store: Store trust is independent of our own signing cert.

### Backlog / Future

- **Native Windows GUI (WinUI 3 / Windows App SDK)** — a fully native Windows front-end (Fluent design,
  deep OS/Store integration, no WebView dependency) for a more polished, Store-grade Windows experience.
  Windows-only by nature; shares `ScadBundler.Core` but is a separate UI from the Blazor app. Higher
  ongoing maintenance (a third UI surface), hence backlog rather than near-term.
- **Sponsor-funded code signing** — Azure Trusted Signing (Windows, ~$10/mo) to remove SmartScreen
  warnings on the portable exe/winget; Apple notarization (macOS, $99/yr) for Gatekeeper. The pipeline
  is built signing-ready so either flips on with no rework once funded (see *Unsigned binaries* below).
- **Optional channels** — Chocolatey (Windows), Linux `.deb` / AppImage / Flatpak. Slot into `release.yml` later.

---

## Distribution channels

One **GitHub Release per tag** is the hub; every channel is fed from it automatically.

| Channel | Delivers | Platforms | Audience | Install | Cost |
| --- | --- | --- | --- | --- | --- |
| **Portable zip + single exe** | desktop app & CLI | win/mac/linux (x64+arm64) | "just give me the app/exe" | download, run / add to PATH | none |
| **Microsoft Store (MSIX)** | desktop app | Windows | non-technical "obtain & trust" | Store → Get | ~$19 one-time |
| **winget** | CLI (and/or app) | Windows | Windows users | `winget install ScadBundler` | none |
| **NuGet `dotnet tool`** | CLI | any with .NET | developers | `dotnet tool install -g ScadBundler` | free account |
| **Homebrew tap** *(optional)* | CLI | mac/linux | mac/linux CLI users | `brew install dano7/tap/scadbundler` | none |

Portable, winget, NuGet, and Homebrew are **zero-cost and fully automated**. The Store is the one
channel needing a one-time paid account; it carries the desktop GUI.

---

## Portable executable: technology choice

Target: artifacts that need **no .NET install**. Two products, two strategies:

- **CLI → Native AOT.** The engine is an ideal AOT candidate — zero NuGet deps, no reflection, no
  `System.Text.Json`, hand-written lexer/parser, hand-rolled CLI parsing. Native AOT yields a true
  single native binary (~3–10 MB), instant startup. Enforced via `<IsAotCompatible>true</IsAotCompatible>`
  + `<InvariantGlobalization>true</InvariantGlobalization>`; with the repo's `TreatWarningsAsErrors`,
  any future code that breaks AOT fails the build loudly. Per-RID fallback to self-contained single-file
  if a target ever balks.
- **Desktop app → self-contained, single-file, trimmed.** Blazor's rendering uses reflection, so the
  desktop app **cannot** use Native AOT. It ships self-contained single-file (~30–80 MB; still no .NET
  install needed). Trimming is applied conservatively (Blazor trimming needs care) and can tighten later.

**Build matrix** (Native AOT must build on its target OS — the matrix handles this):

| RID | Runner |
| --- | --- |
| `win-x64`, `win-arm64` | `windows-latest` (arm64 cross-compiled with ARM64 build tools) |
| `osx-x64`, `osx-arm64` | `macos-latest` |
| `linux-x64` | `ubuntu-latest` |
| `linux-arm64` | `ubuntu-24.04-arm` |

Each job emits `scadbundler-<rid>.zip` (+ a bare exe/binary) and a SHA256 checksum on the Release.

---

## Versioning & release trigger

- **MinVer** (build-time dependency on the *CLI/packaging projects only*, never on Core): version
  derived from the git tag.
- **One trigger:** push annotated tag `vX.Y.Z` → `release.yml` builds every artifact, publishes every
  channel, and cuts the GitHub Release with auto-generated notes. `workflow_dispatch` wired for dry runs.
- Add `CHANGELOG.md` (keep-a-changelog) for human notes.

**A new release is one `git tag` + `git push --tags`.** That is the whole maintenance story.

---

## Unsigned binaries & the sponsorship appeal

Until signing is funded, the portable exe and winget binaries are unsigned, so Windows SmartScreen
shows a one-time *"Windows protected your PC"* prompt (macOS Gatekeeper is similar). The install docs
(README + a `docs/Install.md`) will explain this and ask for help funding signing. Draft blurb:

> ### "Windows protected your PC"
> ScadBundler's downloads aren't code-signed yet, so the first time you run the `.exe`, Windows
> SmartScreen may warn you. This is normal for new, unsigned open-source apps and does **not** mean the
> file is unsafe. To run it: click **More info → Run anyway**. (You can verify your download against the
> SHA256 checksum published with each release.)
>
> **Why isn't it signed?** Code-signing certificates cost money this project doesn't have yet.
> Microsoft's **Azure Trusted Signing** (~$10/month) would make this warning disappear for **every**
> Windows user, and Apple notarization (~$99/year) would do the same on macOS. If ScadBundler saves you
> time, please consider **sponsoring the project** to fund signing — even one sponsor covering the
> Windows cert clears the warning for everyone. 🙏

To make the appeal actionable, set up **GitHub Sponsors** (one-time) and add a `.github/FUNDING.yml`;
the "sponsoring the project" link points there. (Setting up Sponsors is your call — no code needed.)

---

## Publishing as a `dotnet tool` (maintainer instructions)

**One-time setup**
1. Create a free account at **nuget.org** (sign in with Microsoft or GitHub).
2. Confirm the package id **`ScadBundler`** is available; if taken, fall back to e.g. `OpenScadBundler`
   — the *command users type*, `ToolCommandName=scadbundler`, is independent of the package id.
3. Create an **API key** (Account → API Keys): scope **Push**, glob `ScadBundler`, ~1-year expiry.
4. Add it to the GitHub repo as an Actions secret named **`NUGET_API_KEY`**.
5. Fill in the package metadata (Phase 2 foundations).

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
- **Windows:** install the **desktop app** from the **Microsoft Store**, or grab the portable zip from
  Releases (run the app, or use the bundled `scadbundler.exe`), or `winget install ScadBundler` for the CLI.
- **macOS / Linux:** download the portable app/CLI for your arch from Releases, or
  `brew install dano7/tap/scadbundler` for the CLI.

**For developers who already have .NET:**
```bash
dotnet tool install --global ScadBundler        # install
scadbundler bundle myproject.scad -o bundled.scad
dotnet tool update  --global ScadBundler        # update
dotnet tool uninstall --global ScadBundler      # remove
```
Both the desktop app and the CLI read `OPENSCADPATH` + the per-user OpenSCAD library folder
automatically, so existing library layouts work with no extra flags.

---

## Secrets & accounts checklist

| Item | Needed for | When |
| --- | --- | --- |
| `NUGET_API_KEY` (repo secret) | `dotnet tool` publish | Phase 2 |
| Partner Center account (~$19) + reserved name | Microsoft Store | Phase 3 |
| Azure AD app + Partner Center association (repo secrets) | automated Store submission | Phase 3 |
| `dano7/homebrew-tap` repo + a PAT | Homebrew auto-bump | Phase 2 (optional) |
| GitHub Sponsors + `.github/FUNDING.yml` | the signing sponsorship appeal | Phase 2 |
| Azure Trusted Signing (~$10/mo) | Windows signing | **deferred — sponsor-funded** |
| Apple Developer ($99/yr) | macOS notarization | **deferred — sponsor-funded** |

GitHub's built-in `GITHUB_TOKEN` covers creating Releases and uploading assets — no secret needed for that.

## One-time checks before publishing
- Verify the `ScadBundler` package id is free on NuGet (and the Store/winget name).
- Provide an app **icon** + short Store description/screenshots (reuse web app branding).
- Decide the first public version tag (suggest `v0.2.0`, since `0.1.0` was never released).

---

## What implementation sets up (after approval)

1. **Phase 1 (now):** `ScadBundler.UI` RCL extraction from the web app + `desktop/ScadBundler.Desktop`
   (Photino) with `DiskFileSystem` + native pickers + `OPENSCADPATH`.
2. **Phase 2:** csproj/profile/metadata + `global.json` + MinVer + `packaging/` scaffold + `CHANGELOG.md`;
   `release.yml` (matrix build → GitHub Release → NuGet push → winget [→ Homebrew]); `.github/FUNDING.yml`
   + `docs/Install.md` with the SmartScreen/sponsorship blurb.
3. **Phase 3:** Microsoft Store MSIX packaging + submission workflow (gated on your Partner Center account).
4. **Backlog:** native Windows GUI (WinUI 3); sponsor-funded signing; optional Chocolatey/Linux packages.

Existing `ci.yml`, `deploy-pages.yml`, and `codeql.yml` stay as-is.
