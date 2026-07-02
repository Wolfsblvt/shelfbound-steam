# Releasing the tray

The canonical, repeatable process for shipping a new **Shelfbound tray** version. Humans and agents follow
this so releases, notes, and docs stay consistent. Decisions behind it: see the "Packaging & distribution"
section of [DECISIONS.md](./DECISIONS.md).

## The model in one breath

`Directory.Build.props` version → `CHANGELOG.md` → **`tray-v<version>` tag** → the **Release Tray** CI
workflow builds Windows + Linux, publishes them (and the update packages) to a **GitHub Release**, and sets
the Release notes from the changelog. Installed clients **self-update** from that Release on their next check.
The tray links to the Release notes in-app; a future website can render the same GitHub Releases.

- **Version** — one solution version in [`Directory.Build.props`](../../Directory.Build.props). The tag is
  `tray-v<version>` (e.g. `tray-v0.6.0`); that stream is separate from any future CLI/`dotnet tool` release.
- **Release notes** — [`CHANGELOG.md`](../../CHANGELOG.md) is the **single source**. The section for the
  version becomes the GitHub Release body ([Keep a Changelog](https://keepachangelog.com/) format).
- **Distribution** — GitHub Releases (free, fits open-core; no extra hosting). The app updates via Velopack's
  `GithubSource`.

## Cut a release

1. **Land the work** on the branch, with `CHANGELOG.md`'s `[Unreleased]` section kept current as you go.
2. **Prepare** — bump the version, promote the changelog, commit, and tag. Use the helper (does all of it
   locally; it never pushes):

   ```pwsh
   ./scripts/release.ps1 -Version 0.6.1          # add -WhatIf to preview
   ```

   Or by hand: set `<Version>` in `Directory.Build.props`; rename `## [Unreleased]` to
   `## [0.6.1] - <date>` (add a fresh empty `[Unreleased]` above it); commit; `git tag tray-v0.6.1`.
3. **Push the tag** to trigger the release:

   ```pwsh
   git push origin tray-v0.6.1
   ```
4. **Watch CI** (the *Release Tray* run). It publishes a GitHub Release with the Windows `Setup.exe`, the
   Linux `AppImage`, the delta/full update packages, and the changelog notes.
5. **Verify** the Release page looks right and (for a real rollout) that an installed client picks up the
   update. Auto-update only kicks in from the **second** release onward — the first has nothing to update
   from.

> Push the *commit* (to the branch/main) as well per normal git rules; the **tag** is what the workflow
> triggers on.

## Test a build without releasing

Run the workflow via **`workflow_dispatch`** (Actions tab → *Release Tray* → *Run workflow*, or
`gh workflow run release-tray.yml --ref <branch>`). It builds all platforms and uploads the installers as
**Actions artifacts only** — no GitHub Release, no publish. Do this at least once on a branch (or after
merge) before the first real tag, since the Linux/macOS packaging only runs on CI. The workflow file must
exist on the branch you target.

Locally you can also pack Windows to eyeball the installer (outputs to `./Releases`):

```pwsh
dotnet tool install -g vpk --version 1.2.0
dotnet publish src/Shelfbound.Tray -c Release -r win-x64 --self-contained -o publish
vpk pack --packId Shelfbound.Tray --packVersion 0.6.1 --packDir publish --mainExe Shelfbound.Tray.exe --packTitle Shelfbound
```

## CI workflow shape

[`.github/workflows/release-tray.yml`](../../.github/workflows/release-tray.yml): a shared **`version`** job
resolves the version (from the tag, else `Directory.Build.props`), then

| Job | Runner | Output | On a `tray-v*` tag |
| --- | --- | --- | --- |
| `windows` | `windows-latest` | `Setup.exe` (+ update packages) | published to the Release; sets notes from CHANGELOG |
| `linux` | `ubuntu-latest` | `AppImage` (+ update packages) | published to the Release (runs after `windows` to avoid a create-release race) |
| `macos` | `macos-latest` | `.app` (unsigned) | **artifact only** — never published (see Signing) |

## Signing

- **Windows (optional now).** Unsigned installers work but trip SmartScreen. To sign, add repo secrets
  `WINDOWS_CERT_BASE64` (base64 of the `.pfx`) and `WINDOWS_CERT_PASSWORD`; the `windows` job then
  Authenticode-signs automatically. Record the secrets' existence in the private `credentials.md`. A
  standard OV cert works; **[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/)**
  (subscription, no hardware token, Velopack-supported) is the cheaper modern option worth preferring.
- **macOS (required before public distribution).** Gatekeeper blocks un-notarized apps, so macOS ships as an
  unsigned tester artifact only. Enabling it needs an Apple Developer ID cert + notarization
  (`notarytool`) — a **deferred task**. A ready-to-run deep-research brief for the exact current flow is kept
  locally in `.prompts/macos-notarization-research.md` (gitignored); run it, then commit the result to
  `docs/project/research/` and fold the decision here + into `DECISIONS.md`. Publishing to GitHub Releases
  uses the built-in `GITHUB_TOKEN` (no secret).

## Icons (placeholder)

The only in-repo asset is a 32×32 `src/Shelfbound.Tray/Assets/tray.png` (used for the Linux AppImage icon).
A crisp installer needs a larger source: drop a ≥256×256 `icon.png` and pass `--icon icon.ico` (Windows) /
`--icon icon.icns` (macOS) in the pack steps. Deferred until branded assets exist.

## Rules to keep (every release)

- **CHANGELOG is the contract.** Keep `[Unreleased]` current while working; the release promotes it. Never
  hand-write GitHub Release notes that diverge from the changelog.
- **Sync docs in the same change.** If a release changes behavior/scope, update `PROJECT.md` (status/roadmap),
  the tray `README.md`, and add a `DECISIONS.md` entry for any real decision — same rules as all repo docs
  ([AGENTS.md](../../AGENTS.md)).
- **Public repo hygiene.** No secrets, no proprietary/hosted-product detail in history.
- **Don't push unless asked**; the tag push is the deliberate trigger.
