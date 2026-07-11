# Releasing Shelfbound

The canonical, repeatable processes for the immutable **library packages** and the **Shelfbound tray**.
Humans and agents follow these so releases, notes, source identity, and docs stay consistent. Decisions
behind them: see the "Packaging & distribution" section of [DECISIONS.md](./DECISIONS.md).

## Open-core library packages

`Directory.Build.props` binds the current library package version to its snapshot schema version.
For this release, `Shelfbound.Core`, `Shelfbound.Query`, and `Shelfbound.Steam` are package `0.7.0`
and produce schema `0.5.0`. `Shelfbound.Storage` is local persistence and remains non-packable; the
portable `SnapshotStorage` DTO is already in Core.

The `v<version>` tag triggers `nuget-publish.yml`. The workflow:

1. runs the complete solution with warnings as errors;
2. exercises the producer rejection gates (reused version, schema change without package bump, and an
   unapproved public-API break);
3. package-validates against the previous published release and verifies nuspec package/schema/commit;
4. packs only Core/Query/Steam; and
5. pushes through Trusted Publishing **without** `--skip-duplicate`.

The cloud repository enforces the fourth gate (a stale consumer pin), then independently packs this
exact producer commit to a unique throwaway version in a local feed, restores its consumer against that
feed, and sends old/current/next golden JSON through `SnapshotSerializer` + `/ingest`. A missing producer
checkout or skipped HTTP test is a failure.

### Owner publish steps after both repositories merge

Merge the producer first, then the cloud consumer so cloud CI can pack steam `main`. On clean `main`
checkouts, run the gates once more:

```pwsh
dotnet test Shelfbound.slnx -c Release -warnaserror
pwsh scripts/test-package-release-gates.ps1
pwsh scripts/test-package-release.ps1 -CloudRepo <path-to-shelfbound-cloud>
```

Then create and push the immutable library tag (the implementation agent does not do this):

```pwsh
git tag -a v0.7.0 -m "Shelfbound libraries 0.7.0 (snapshot schema 0.5.0)"
git push origin v0.7.0
```

The publish workflow requires `v0.7.0` to point at the commit whose `Directory.Build.props` says
`0.7.0`, and requires all three package ids to be absent at that version. After the workflow succeeds,
verify each nuget.org page reports `0.7.0`, schema `0.5.0` in release notes, the tagged repository commit,
and a symbol package.

## Releasing the tray

### The model in one breath

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

### Before the first *public* release (deferrable)

The tray builds, self-updates, and runs locally today. These are only needed before you hand installers to
real users — safe to defer until then:

- [ ] **Production URLs.** `AppSettings` defaults `ServerUrl`/`WebAppUrl` to `localhost`. Point them at the
      real Shelfbound endpoints (or add a first-run config) before shipping — otherwise Connect/Sync do
      nothing on an installed build. The URLs are hosted-product config, deliberately not hardcoded here.
- [ ] **Windows code signing** (optional but removes SmartScreen warnings) — see [Signing](#signing).
- [ ] **macOS signing + notarization** — required before macOS is public — see [Signing](#signing).
- [ ] **Branded icons** — replace the placeholder — see [Icons](#icons).

None block local use or a Windows/Linux beta.

### Cut a release

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

### Test a build without releasing

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

### CI workflow shape

[`.github/workflows/release-tray.yml`](../../.github/workflows/release-tray.yml): a shared **`version`** job
resolves the version (from the tag, else `Directory.Build.props`), then

| Job | Runner | Output | On a `tray-v*` tag |
| --- | --- | --- | --- |
| `windows` | `windows-latest` | `Setup.exe` (+ update packages) | published to the Release; sets notes from CHANGELOG |
| `linux` | `ubuntu-latest` | `AppImage` (+ update packages) | published to the Release (runs after `windows` to avoid a create-release race) |
| `macos` | `macos-latest` | `.app` (unsigned) | **artifact only** — never published (see Signing) |

### Signing

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

### Icons

**`assets/icon.svg` is the single source of truth** (a placeholder mark today; drop in branded artwork later,
same filename). Neither Avalonia's tray icon nor the OS installers consume SVG directly — they need raster at
fixed sizes — so we **generate-and-commit** the rasters rather than rasterize on every build (keeps CI free of
an SVG toolchain, and macOS `.icns` needs macOS tooling anyway):

```pwsh
./scripts/icons.ps1        # requires ImageMagick; regenerates the raster icons from the SVG
```

That produces `src/Shelfbound.Tray/Assets/tray.png` (in-app icon), `assets/icon-256.png` (Linux AppImage),
`assets/icon.ico` (Windows — auto-picked up by the pack step when present), and best-effort `assets/icon.icns`
(prefer `iconutil` on a Mac for crisp results). Commit the regenerated files. Until they're generated the
installers fall back to Velopack's default icon — nothing breaks.

## CLI / MCP tools (separate release stream)

The `shelfbound` CLI and `shelfbound-mcp` server ship as **.NET global tools**. Their versions come from
their own `.csproj` (`Shelfbound.Cli` 0.1.0, `Shelfbound.Mcp` 0.3.0), independent of the tray and library
packages. Users install with `dotnet tool install -g Shelfbound.Cli` / `Shelfbound.Mcp`.

> First publish of a new package id needs the nuget.org Trusted Publishing policy to cover it (the
> `Shelfbound.*` prefix / `nuget` environment). Library `v*` tags deliberately exclude these tools so
> an unchanged tool can never be a silent duplicate; add a project-scoped tag/workflow before the next
> tool update.

## Rules to keep (every release)

- **CHANGELOG is the contract.** Keep `[Unreleased]` current while working; the release promotes it. Never
  hand-write GitHub Release notes that diverge from the changelog.
- **Sync docs in the same change.** If a release changes behavior/scope, update `PROJECT.md` (status/roadmap),
  the tray `README.md`, and add a `DECISIONS.md` entry for any real decision — same rules as all repo docs
  ([AGENTS.md](../../AGENTS.md)).
- **Public repo hygiene.** No secrets, no proprietary/hosted-product detail in history.
- **Don't push unless asked**; the tag push is the deliberate trigger.
