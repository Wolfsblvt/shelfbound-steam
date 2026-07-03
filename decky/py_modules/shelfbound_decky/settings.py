"""Plugin settings + device-token persistence (Decky settings dir).

The settings file holds no secrets — the device token lives in a separate 0600 file,
mirroring the tray's TokenStore posture on Linux. The token never crosses the
frontend bridge either; it stays backend-side.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path

# Matches the tray's dev default (src/Shelfbound.Tray/AppSettings.cs). Production URLs
# are deliberately not committed yet — a known pre-public task in docs/project/PROJECT.md.
DEFAULT_SERVER_URL = "http://localhost:5080"

SETTINGS_FILENAME = "settings.json"
TOKEN_FILENAME = "token"


@dataclass
class PluginSettings:
    server_url: str = DEFAULT_SERVER_URL
    device_name: str | None = None
    # {"at": iso, "status": str, "message": str | None, "gameCount": int | None}
    last_sync: dict | None = None


class SettingsStore:
    def __init__(self, directory: str) -> None:
        self._path = Path(directory) / SETTINGS_FILENAME

    def load(self) -> PluginSettings:
        try:
            raw = json.loads(self._path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            # Missing or corrupt file: fall back to defaults rather than failing the plugin.
            return PluginSettings()

        server_url = raw.get("serverUrl")
        return PluginSettings(
            server_url=server_url if isinstance(server_url, str) and server_url.strip() else DEFAULT_SERVER_URL,
            device_name=raw.get("deviceName") if isinstance(raw.get("deviceName"), str) else None,
            last_sync=raw.get("lastSync") if isinstance(raw.get("lastSync"), dict) else None,
        )

    def save(self, settings: PluginSettings) -> None:
        payload: dict = {"serverUrl": settings.server_url}
        if settings.device_name is not None:
            payload["deviceName"] = settings.device_name
        if settings.last_sync is not None:
            payload["lastSync"] = settings.last_sync

        self._path.parent.mkdir(parents=True, exist_ok=True)
        self._path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


class TokenStore:
    """Device API token in a user-only file. Best-effort: unreadable == not connected."""

    def __init__(self, directory: str) -> None:
        self._path = Path(directory) / TOKEN_FILENAME

    def save(self, token: str) -> None:
        self._path.parent.mkdir(parents=True, exist_ok=True)
        self._path.write_text(token, encoding="utf-8")
        try:
            os.chmod(self._path, 0o600)
        except OSError:
            pass  # permission tightening is best-effort (and a no-op on Windows dev machines)

    def load(self) -> str | None:
        try:
            token = self._path.read_text(encoding="utf-8").strip()
            return token or None
        except OSError:
            return None

    def clear(self) -> None:
        try:
            self._path.unlink()
        except OSError:
            pass  # nothing to clear
