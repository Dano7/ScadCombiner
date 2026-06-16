# Releasing ScadBundler

Releases are **fully automated from a git tag**. Versions are derived from the tag by
[MinVer](https://github.com/adamralph/minver) (`MinVerTagPrefix=v`), so there is no version number
to bump in source.

## Cut a release

```bash
git tag v0.2.0        # annotated or lightweight; must start with 'v'
git push origin v0.2.0
```

That triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml), which:

1. **Portable binaries** — publishes self-contained, single-file, trimmed executables for
   `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` (cross-compiled from a
   single Windows runner — which also builds the MSIX, since `makeappx` is Windows-only), zips each
   with `LICENSE`, and writes `SHA256SUMS.txt`.
2. **NuGet `dotnet tool`** — packs and (via **Trusted Publishing**, when the `NUGET_USER` repo
   variable is set) pushes to NuGet.org.
3. **GitHub Release** — creates the release for the tag and attaches the zips, the bare Windows
   `.exe`s, the checksums, and the `.nupkg`.
4. **winget** — (if `WINGET_TOKEN` is set) opens a PR to `microsoft/winget-pkgs`.
5. **MSIX** — builds an (unsigned) MSIX and attaches it. *Preview; see below.*

### Dry run first
Run **Actions → Release → Run workflow** from a branch. It builds and uploads every artifact (as a
workflow artifact) but creates **no** GitHub Release and publishes nothing — the publish steps are
gated on a version tag (`github.ref_type == 'tag'`). Do this before the first real tag to shake out
the matrix and the MSIX step.

## One-time setup

| Setting | Enables | How |
| --- | --- | --- |
| `NUGET_USER` (repo **variable**) | NuGet publish via Trusted Publishing | See below — OIDC, no API key |
| `WINGET_TOKEN` (repo secret) | winget PRs | A classic PAT with `public_repo`, from an account that has forked `microsoft/winget-pkgs` |
| GitHub Sponsors | the signing appeal in [Install.md](Install.md) | Enable Sponsors for the account; `.github/FUNDING.yml` is already in place |
| Partner Center (~$19) | Microsoft Store | Reserve the **ScadBundler** name; then wire Store submission (see below) |

When these are unset, the matching steps **skip cleanly** — the GitHub Release and portable binaries
still publish. Nothing goes to NuGet/winget/Store until you configure them.

### NuGet — Trusted Publishing (OIDC, no API key)
The workflow uses [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
GitHub's OIDC token is swapped for a short-lived NuGet key at run time, so there is **no
`NUGET_API_KEY` secret** to manage or leak.

1. **On nuget.org** (signed in): *Account → Trusted Publishing → Add* a policy with
   Package owner = your account, Package = `ScadBundler`, Repository owner = `Dano7`,
   Repository = `ScadCombiner`, Workflow file = `release.yml`, Environment = *(blank)*.
2. **On GitHub:** Settings → Secrets and variables → **Actions → Variables** → add variable
   `NUGET_USER` = your nuget.org username. (A *variable*, not a secret — the username isn't sensitive.)
3. Done — the workflow already has `permissions: id-token: write` and the `NuGet/login@v1` step.

> **First publish of a brand-new id:** Trusted Publishing policies bind to an id you own/​reserve. If
> nuget.org rejects the first OIDC push because the package doesn't exist yet, run `dotnet nuget push`
> once with a temporary API key, then rely on Trusted Publishing for every release after.

## Pre-publish checklist (first release only)
- Confirm the **`ScadBundler`** package id is free on [nuget.org](https://www.nuget.org/packages/ScadBundler)
  (the command name `scadbundler` is independent of the id). If taken, change `<PackageId>` in
  [`src/ScadBundler/ScadBundler.csproj`](../src/ScadBundler/ScadBundler.csproj).
- First winget submission to `microsoft/winget-pkgs` goes through review; subsequent ones are
  auto-bumped by the action. winget ids must be `Publisher.Package` — the workflow uses
  `DanOlsen.ScadBundler`; change the publisher moniker in `release.yml` if you prefer another.
- Replace the placeholder MSIX assets in `packaging/msix/Images/` with real branding.

## MSIX & Microsoft Store (preview)

`packaging/msix/` holds the manifest, placeholder logos, and `build-msix.ps1`. The `release.yml`
`msix` job builds an **unsigned** package (marked `continue-on-error` so a hiccup never blocks the
rest of the release). An unsigned MSIX can't be installed by double-click; the clean path is the
**Microsoft Store**, which signs the package for free at ingestion.

To enable the Store once you have a Partner Center account:
1. Reserve the app name and set `Identity/@Name` + `Publisher` in `packaging/msix/AppxManifest.xml`
   to the values Partner Center assigns.
2. Add an Azure AD app + Partner Center association and wire the
   [`microsoft/store-submission`](https://github.com/microsoft/store-submission) action (secrets:
   tenant/client/credential). This is intentionally left dormant until the account exists.

## Native AOT (future optimization)
The portable exe currently ships as self-contained single-file + trim (~11 MB, verified). Native
AOT would shrink it further and speed startup, but needs per-OS native toolchains (MSVC / clang /
Xcode) validated on each runner. It's a drop-in switch via `-p:PublishAot=true` once validated — see
[Distribution.md](Distribution.md).
