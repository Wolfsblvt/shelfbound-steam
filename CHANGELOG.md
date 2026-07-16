# Changelog

Notable changes to the **Shelfbound tray agent**, newest first. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions match `Directory.Build.props` and the
`tray-v<version>` release tags.

This file is the **single source of release notes**: the section for a version becomes that GitHub Release's
body, which the tray links to as "Release notes" and a future website can render. Keep the `[Unreleased]`
section up to date as you work — the release step promotes it to a new version. See
[docs/project/releasing.md](docs/project/releasing.md) for the full flow.

## [Unreleased]
### Added
- **Updates** section in the window: check for updates on demand, see when it last checked, and a toggle
  for automatic checks.
- "Report a bug" link in the footer.

### Changed
- Redesigned the window as a two-column dashboard so everything fits without a scrollbar.
- The Account card now shows this device and `Connected (upload-only)`; account and plan details stay in
  the web dashboard because the tray token can only upload this device's snapshots.

### Fixed
- The sync interval is greyed out when automatic background sync is off.

### Security
- Replaced browser-returned long-lived tokens with a short-lived one-time code. The tray validates an exact
  numeric-loopback callback, redeems the code directly, and stores a device-bound upload-only token; a bearer
  never enters browser URLs, history, or callback records.
- Limited GitHub Release write permission to the Windows and Linux tray publishing jobs; all other release-workflow
  jobs inherit read-only repository access.
- Routed tray-release event/ref data through runner environment variables and an exact validated tag output, preventing
  adversarial ref names from becoming shell source or a GitHub Release target.

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
