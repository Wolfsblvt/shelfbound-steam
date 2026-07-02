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
- The **Account** card shows who's signed in, your plan and device allowance, and this device's name. A
  **"Manage devices in dashboard"** button opens the web dashboard for the full device list and revocation.
  **Sign out** clears the local token and stops auto-sync immediately; the server-side token expires at 90 days
  or can be revoked from the dashboard. Plan limits are enforced by the server — the tray only displays them,
  it never gates features client-side.
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

The tray ships via the **Release Tray** GitHub Actions workflow: bump the version, promote `CHANGELOG.md`,
push a **`tray-v<version>`** tag, and CI publishes the Windows + Linux installers, the update packages, and
the changelog notes to a GitHub Release that clients self-update from.

Full process, CI shape, signing, and the release rules:
**[docs/project/releasing.md](../../docs/project/releasing.md)**. The `./scripts/release.ps1 -Version <x.y.z>`
helper does the local prep (version bump, changelog, commit, tag); `workflow_dispatch` builds installers as
artifacts for testing without publishing.
