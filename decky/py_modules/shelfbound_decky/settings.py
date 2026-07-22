"""Plugin settings + device-token persistence (Decky settings dir).

The settings file holds no secrets — the device token lives in a separate 0600 file,
mirroring the tray's TokenStore posture on Linux. The token never crosses the
frontend bridge either; it stays backend-side.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

from .private_file import write_private_text

# Matches the tray's dev default (src/Shelfbound.Tray/AppSettings.cs). Production URLs
# are deliberately not committed yet — a known pre-public task in docs/project/PROJECT.md.
DEFAULT_SERVER_URL = "http://127.0.0.1:5080"

SETTINGS_FILENAME = "settings.json"
TOKEN_FILENAME = "token"
MAX_SETTINGS_FILE_BYTES = 1024 * 1024
MAX_PRIVATE_GAME_OVERRIDES = 10_000


@dataclass
class PluginSettings:
    server_url: str = DEFAULT_SERVER_URL
    device_name: str | None = None
    # {"at": iso, "status": str, "message": str | None, "gameCount": int | None}
    last_sync: dict | None = None
    exclude_steam_private_games: bool = False
    private_game_unskip_app_ids: set[int] = field(default_factory=set)


class SettingsStore:
    def __init__(self, directory: str) -> None:
        self._path = Path(directory) / SETTINGS_FILENAME

    def load(self) -> PluginSettings:
        try:
            if self._path.stat().st_size > MAX_SETTINGS_FILE_BYTES:
                return PluginSettings()
            raw = json.loads(self._path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            # Missing or corrupt file: fall back to defaults rather than failing the plugin.
            return PluginSettings()

        if not isinstance(raw, dict):
            return PluginSettings()

        server_url = raw.get("serverUrl")
        raw_overrides = raw.get("privateGameUnskipAppIds")
        overrides = {
            value
            for value in raw_overrides
            if isinstance(value, int) and not isinstance(value, bool) and value > 0
        } if isinstance(raw_overrides, list) else set()
        return PluginSettings(
            server_url=server_url if isinstance(server_url, str) and server_url.strip() else DEFAULT_SERVER_URL,
            device_name=raw.get("deviceName") if isinstance(raw.get("deviceName"), str) else None,
            last_sync=raw.get("lastSync") if isinstance(raw.get("lastSync"), dict) else None,
            exclude_steam_private_games=raw.get("excludeSteamPrivateGames") is True,
            private_game_unskip_app_ids=set(sorted(overrides)[:MAX_PRIVATE_GAME_OVERRIDES]),
        )

    def save(self, settings: PluginSettings) -> None:
        payload: dict = {
            "serverUrl": settings.server_url,
            "excludeSteamPrivateGames": settings.exclude_steam_private_games,
        }
        if settings.device_name is not None:
            payload["deviceName"] = settings.device_name
        if settings.last_sync is not None:
            payload["lastSync"] = settings.last_sync
        overrides = settings.private_game_unskip_app_ids or set()
        if overrides:
            payload["privateGameUnskipAppIds"] = sorted(
                app_id for app_id in overrides if app_id > 0
            )[:MAX_PRIVATE_GAME_OVERRIDES]

        write_private_text(self._path, json.dumps(payload, indent=2))


class TokenStore:
    """Device API token in a user-only file. Best-effort: unreadable == not connected."""

    def __init__(self, directory: str) -> None:
        self._path = Path(directory) / TOKEN_FILENAME

    def save(self, token: str) -> None:
        write_private_text(self._path, token)

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
