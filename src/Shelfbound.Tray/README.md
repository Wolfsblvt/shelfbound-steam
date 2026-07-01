# Shelfbound.Tray

The cross-platform Shelfbound tray agent (Avalonia). It keeps your Steam library synced to a Shelfbound
server in the background, shows a quick status and your account, and lets you connect without copy-pasting a
token.

## Install

- **Windows:** download `Shelfbound.Tray-win-Setup.exe` from
  [GitHub Releases](https://github.com/Wolfsblvt/shelfbound-steam/releases) and run it. It installs per-user,
  adds Start-menu/Desktop shortcuts, and **auto-updates** in the background from later releases. Linux
  (AppImage/Flatpak) and macOS installers are planned follow-ups.
- **From source (any OS):**

  ```bash
  dotnet run --project src/Shelfbound.Tray
  ```

  Source/dev runs never self-update — only installed builds do.

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
([.github/workflows/release-tray.yml](../../.github/workflows/release-tray.yml)):

- Push a `tray-v<version>` tag (e.g. `tray-v0.6.0`, matching `Directory.Build.props`) to build the installer
  and publish it plus the delta/full update packages to GitHub Releases. This tag stream is kept separate
  from any CLI/`dotnet tool` release so their assets never collide.
- `workflow_dispatch` builds the installer as a downloadable Actions artifact only (no Release published) —
  for testing before you tag.
- **Code signing is optional:** set the `WINDOWS_CERT_BASE64` (base64 of the `.pfx`) and
  `WINDOWS_CERT_PASSWORD` repo secrets to sign; without them the installer is unsigned. Publishing uses the
  built-in `GITHUB_TOKEN`.

Build and pack locally to test the pipeline (outputs to `./Releases`):

```bash
dotnet tool install -g vpk --version 1.2.0
dotnet publish src/Shelfbound.Tray -c Release -r win-x64 --self-contained -o publish
vpk pack --packId Shelfbound.Tray --packVersion 0.6.0 --packDir publish --mainExe Shelfbound.Tray.exe --packTitle Shelfbound
```
