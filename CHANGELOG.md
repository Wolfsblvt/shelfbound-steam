# Changelog

Notable changes to the **Shelfbound tray agent**, newest first. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions match `Directory.Build.props` and the
`tray-v<version>` release tags.

This file is the **single source of release notes**: the section for a version becomes that GitHub Release's
body, which the tray links to as "Release notes" and a future website can render. Keep the `[Unreleased]`
section up to date as you work — the release step promotes it to a new version. See
[docs/project/releasing.md](docs/project/releasing.md) for the full flow.

## [Unreleased]

## [0.6.0] - 2026-07-02
### Added
- **Installer + auto-update** — the tray ships as a [Velopack](https://velopack.io) installer that
  self-updates in the background from GitHub Releases (Windows `Setup.exe`, Linux `AppImage`).
- **Account awareness** — the tray shows the signed-in Shelfbound account, your plan and device allowance,
  and your connected devices, with a **Sign out** that revokes this device's token. Display-only; the server
  still enforces plan limits.
- **In-app version + release notes** — the window shows the running version and links to the release notes.

### Notes
- Auto-update takes effect from the **next** release onward (a prior version must exist to update from).
- macOS builds are **unsigned** test artifacts until signing + notarization are set up; not for public use yet.
