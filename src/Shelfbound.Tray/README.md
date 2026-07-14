# Shelfbound.Tray

The cross-platform Shelfbound tray agent (Avalonia). It keeps your Steam library synced to a Shelfbound
server in the background, shows a quick status for this device, and lets you connect without copy-pasting a
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

- **Connect account** (tray menu or window button) opens the dashboard in your browser to sign in. The browser
  returns a short-lived one-time code to an exact numeric-loopback callback; the tray redeems it directly for
  a device-bound, upload-only token. A bearer never enters browser navigation, history, or callback URLs.
- The **Account** card intentionally shows only this device's bound name and **"Connected (upload-only)"**.
  The device token cannot read account, plan, library, or MCP data. A **"Manage devices in dashboard"** button
  opens the web dashboard for the full device list and revocation. **Sign out** clears the local token and stops
  auto-sync immediately; the server-side token expires at 90 days or can be revoked from the dashboard.
- **Sync now** first builds the privacy-minimized hosted projection and shows its exact compact JSON.
  Confirming sends that same prepared body — the tray does not rescan or reserialize between preview
  and transport.
- New installs default auto-sync off. Background sync can run on an interval after the user enables it
  and has previewed + successfully sent the current projection version once. An uploaded field-set
  expansion or material purpose change invalidates that consent; projection v2 does so for the changed
  `stats.scope` coverage meaning without adding a field.
- Closing the window hides it to the tray. Auto-start on login and background auto-sync are optional
  and configurable independently.

## Hosted privacy boundary

The tray and CLI share `Shelfbound.Client.HostedProjection`; there is no tray-specific payload builder.
The hosted body includes the friendly/neutral device label, random device id, coarse OS/specs, library/
game/category data and stats. It omits every Steam-account field (`accountName`, `personaName`,
`steamId64`), machine hostnames, exact OS builds, full paths, credentials, and hardware serials. The
default device label is the neutral `Shelfbound device`; set a friendly label instead of relying on a
hostname. Game and collection names are still personal data.

## Storage

- Settings live in `…/AppData/shelfbound/tray.json` (server URLs default to localhost for now); the upload-only
  device token is stored separately in `token.bin` — DPAPI-encrypted on Windows, a 0600 file elsewhere.
- The settings file also records only the consented hosted-projection version; it does not duplicate the
  preview body.
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
