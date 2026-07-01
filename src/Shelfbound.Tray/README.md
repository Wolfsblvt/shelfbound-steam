# Shelfbound.Tray

The cross-platform Shelfbound tray agent (Avalonia). It keeps your Steam library synced to a Shelfbound
server in the background, shows a quick status and your account, and lets you connect without copy-pasting a
token.

## Install

- **Windows:** download `Shelfbound.Tray-win-Setup.exe` from
  [GitHub Releases](https://github.com/Wolfsblvt/shelfbound-steam/releases) and run it. Installs per-user,
  adds Start-menu/Desktop shortcuts, and **auto-updates** in the background from later releases.
- **Linux:** download the `.AppImage`, make it executable (`chmod +x`), and run it. It self-updates in place.
  Needs FUSE (`sudo apt install libfuse2` on Ubuntu 24.04+). `linux-x64` also covers the Steam Deck's
  desktop mode.
- **macOS:** unsigned test builds only for now — Gatekeeper will warn until a signed/notarized build ships.
- **From source (any OS):**

  ```bash
  dotnet run --project src/Shelfbound.Tray
  ```

  Source/dev runs never self-update — only installed builds do.

| Platform | Artifact | Status |
| --- | --- | --- |
| Windows x64 | `Setup.exe` | Shipping — auto-update |
| Linux x64 | `AppImage` | Shipping — auto-update |
| macOS arm64 | `.app` (unsigned) | Testing — needs signing + notarization |

## What it does

- **Connect account** (tray menu or window button) opens the dashboard in your browser to sign in; the
  device token is handed back to the app over a localhost callback and saved locally.
- The **Account** card shows who's signed in, your plan and device allowance, and your connected devices.
  **Sign out** revokes this device's token on the server and clears it locally. Plan limits are enforced by
  the server — the tray only displays them, it never gates features client-side.
- **Sync now** uploads immediately; auto-sync runs on an interval when enabled.
- Closing the window hides it to the tray. Auto-start on login and background auto-sync are optional and on
  by default.

## Storage

- Settings live in `…/AppData/shelfbound/tray.json` (server URLs default to localhost for now); the API
  token is stored separately in `token.bin` — DPAPI-encrypted on Windows, a 0600 file elsewhere.
- Login auto-start is wired for Windows (Run key), Linux (`~/.config/autostart`), and macOS (LaunchAgent).

## Auto-update

Installed builds self-update from GitHub Releases via [Velopack](https://velopack.io): on launch (and via the
tray's *Check for updates…*) the agent checks the newest release, downloads it in the background, and offers
a **Restart to update**. `VelopackApp.Build().Run()` runs first in `Program.Main` to handle the installer's
install/update/uninstall hooks.

## Releasing (maintainers)

Releases are built by the **Release Tray** GitHub Actions workflow
([.github/workflows/release-tray.yml](../../.github/workflows/release-tray.yml)) — a shared `version` job
plus `windows`, `linux`, and `macos` build jobs.

**Cut a release:**

1. Bump `<Version>` in [`Directory.Build.props`](../../Directory.Build.props) and commit.
2. Tag it `tray-v<version>` (matching that version) and push the tag:

   ```bash
   git tag tray-v0.6.0
   git push origin tray-v0.6.0
   ```
3. The workflow builds **Windows + Linux** and publishes them — plus the delta/full update packages — to a
   GitHub Release for that tag. Installed clients pick up the update on their next check (auto-update works
   from the *second* release onward, once a prior version exists to update from).

`workflow_dispatch` runs the same build but uploads the installers as **Actions artifacts only** (no Release)
— use it to smoke-test a build before tagging. The `tray-v*` tag stream is separate from any CLI/`dotnet
tool` release so their assets never collide.

**Signing:**

- **Windows** — optional Authenticode: set the `WINDOWS_CERT_BASE64` (base64 of the `.pfx`) and
  `WINDOWS_CERT_PASSWORD` repo secrets to sign; without them the installer is unsigned. Publishing uses the
  built-in `GITHUB_TOKEN` (no secret needed).
- **macOS** — a signed **and notarized** build (Apple Developer ID + `notarytool`) is required before public
  distribution; until then the `macos` job produces an unsigned artifact for testers only.
- Branded installer icons (`.ico` for Windows, `.icns` for macOS) are a follow-up; Linux uses the tray PNG.

Build and pack locally to test the pipeline (outputs to `./Releases`; swap `-r`/`--mainExe` per platform):

```bash
dotnet tool install -g vpk --version 1.2.0
dotnet publish src/Shelfbound.Tray -c Release -r win-x64 --self-contained -o publish
vpk pack --packId Shelfbound.Tray --packVersion 0.6.0 --packDir publish --mainExe Shelfbound.Tray.exe --packTitle Shelfbound
```
