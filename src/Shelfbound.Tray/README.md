# Shelfbound.Tray

The cross-platform Shelfbound tray agent (Avalonia). It keeps your Steam library synced to a Shelfbound
server in the background, shows a quick status, and lets you connect your account without copy-pasting a
token.

```bash
dotnet run --project src/Shelfbound.Tray
```

- **Connect account** (tray menu or window button) opens the dashboard in your browser to sign in; the
  device token is handed back to the app over a localhost callback and saved locally.
- **Sync now** uploads immediately; auto-sync runs on an interval when enabled.
- Closing the window hides it to the tray. Auto-start on login and background auto-sync are optional and
  on by default.
- Settings live in `…/AppData/shelfbound/tray.json` (server URLs default to localhost for now); the API
  token is stored separately in `token.bin` — DPAPI-encrypted on Windows, a 0600 file elsewhere.
- Login auto-start is wired for Windows (Run key), Linux (`~/.config/autostart`), and macOS (LaunchAgent).
