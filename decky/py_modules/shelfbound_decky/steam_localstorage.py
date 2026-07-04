"""Locates the Steam client's Chromium "Local Storage" LevelDB directory.

Mirrors src/Shelfbound.Steam/Collections/SteamLocalStorageLocator.cs, but carries only
the Linux/SteamOS candidates — this plugin never runs on Windows/macOS. This is the
desktop client's web cache where *modern* collections live, NOT under the Steam install
root's steamapps. Override with SHELFBOUND_STEAM_LOCALSTORAGE for non-standard installs
or off-Deck testing.

[NEEDS-DECK / A1] The exact SteamOS path is the one hardware-TBD seam in this port. The
candidates below mirror the validated Windows layout's Linux equivalents plus the known
Steam config locations; on a real Deck this should resolve directly or be a one-line
path fix. The env override makes the whole reader testable without a Deck.
"""

from __future__ import annotations

import os
from pathlib import Path

OVERRIDE_ENV_VAR = "SHELFBOUND_STEAM_LOCALSTORAGE"

# The client's Chromium Local Storage lives under <steam-config>/htmlcache on Linux.
_TAIL = ("config", "htmlcache", "Local Storage", "leveldb")


def locate(steam_root: str | None = None) -> str | None:
    """Returns the Local Storage leveldb dir, or None if no candidate exists.

    Resolution order (mirrors the C# locator): the SHELFBOUND_STEAM_LOCALSTORAGE env
    override, then the dir derived from the already-resolved `steam_root` (the most
    reliable candidate — root discovery is validated separately), then well-known
    SteamOS/Linux locations.
    """
    override = os.environ.get(OVERRIDE_ENV_VAR)
    if override and override.strip():
        return override if os.path.isdir(override) else None

    for candidate in _candidate_paths(steam_root):
        if os.path.isdir(candidate):
            return candidate
    return None


def _candidate_paths(steam_root: str | None) -> list[str]:
    candidates: list[str] = []
    if steam_root and steam_root.strip():
        candidates.append(os.path.join(steam_root, *_TAIL))

    home = Path.home()
    candidates.extend([
        str(home.joinpath(".local", "share", "Steam", *_TAIL)),  # SteamOS / standard Linux
        str(home.joinpath(".steam", "steam", *_TAIL)),
        # flatpak
        str(home.joinpath(".var", "app", "com.valvesoftware.Steam",
                          ".local", "share", "Steam", *_TAIL)),
    ])
    return candidates
