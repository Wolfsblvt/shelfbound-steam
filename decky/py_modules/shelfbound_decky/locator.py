"""Locates the active Steam installation root on SteamOS / Linux.

Resolution order mirrors the C# SteamInstallLocator: explicit override, the
SHELFBOUND_STEAM_PATH env var, then well-known install locations. Only the Linux /
SteamOS candidates are carried over — this plugin never runs on Windows/macOS.
"""

from __future__ import annotations

import os
from pathlib import Path

STEAM_PATH_ENV = "SHELFBOUND_STEAM_PATH"


def is_steam_root(path: str) -> bool:
    """True if the directory looks like a Steam root (contains a steamapps folder)."""
    if not path or not path.strip():
        return False
    return os.path.isdir(os.path.join(path, "steamapps"))


def locate(override_path: str | None = None) -> str | None:
    if override_path and override_path.strip():
        return override_path if is_steam_root(override_path) else None

    env = os.environ.get(STEAM_PATH_ENV)
    if env and env.strip() and is_steam_root(env):
        return env

    for candidate in _candidate_paths():
        if is_steam_root(candidate):
            return candidate
    return None


def _candidate_paths() -> list[str]:
    home = Path.home()
    return [
        str(home / ".local" / "share" / "Steam"),  # SteamOS / standard Linux (the Deck's real root)
        str(home / ".steam" / "steam"),
        str(home / ".steam" / "root"),
        str(home / ".var" / "app" / "com.valvesoftware.Steam" / ".local" / "share" / "Steam"),  # flatpak
    ]
