"""Builds a realistic Deck-shaped Steam root in a temp directory for tests.

Two libraries (an "internal" one and an "SD card" one), two accounts, legacy
sharedconfig categories, one fully-installed game per library plus a
partially-installed game and a missing manifest — enough to exercise every branch
of the scanner without real hardware.
"""

from __future__ import annotations

from pathlib import Path

MOST_RECENT_STEAM_ID = "76561198000000001"
MOST_RECENT_ACCOUNT_ID = 39734273  # 76561198000000001 - 76561197960265728
OTHER_STEAM_ID = "76561198000000002"

MISSING_MANIFEST_APP_ID = 999999


def _manifest(app_id: int, name: str, state_flags: int, install_dir: str,
              size: int, last_updated: int, last_played: int) -> str:
    return f'''"AppState"
{{
\t"appid"\t\t"{app_id}"
\t"Universe"\t\t"1"
\t"name"\t\t"{name}"
\t"StateFlags"\t\t"{state_flags}"
\t"installdir"\t\t"{install_dir}"
\t"LastUpdated"\t\t"{last_updated}"
\t"SizeOnDisk"\t\t"{size}"
\t"buildid"\t\t"1234567"
\t"LastPlayed"\t\t"{last_played}"
}}
'''


INTERNAL_GAMES = [
    # (app_id, name, state_flags, install_dir, size, last_updated, last_played)
    (228980, "Steamworks Common Redistributables", 4, "Steamworks Shared", 418827771, 1746100800, 0),
    (753640, "Outer Wilds", 4, "Outer Wilds", 11652599956, 1746100800, 1750000000),
]

SD_GAMES = [
    (1145360, "Hades", 4, "Hades", 15000000000, 1748000000, 1750500000),
    (1091500, "Cyberpunk 2077", 1026, "Cyberpunk 2077", 68000000000, 1749000000, 0),  # updating, not fully installed
]


def make_steam_root(root: Path) -> dict:
    """Creates the fixture tree; returns paths and expectations for assertions."""
    steam_root = root / "steam"
    internal_lib = steam_root  # library 0 is the Steam root itself, like on a real install
    sd_lib = root / "sdcard" / "steamlib"

    for library, games in ((internal_lib, INTERNAL_GAMES), (sd_lib, SD_GAMES)):
        steamapps = library / "steamapps"
        steamapps.mkdir(parents=True, exist_ok=True)
        for app_id, name, flags, install_dir, size, updated, played in games:
            (steamapps / f"appmanifest_{app_id}.acf").write_text(
                _manifest(app_id, name, flags, install_dir, size, updated, played),
                encoding="utf-8",
            )

    internal_apps = "\n".join(
        f'\t\t\t"{app_id}"\t\t"{size}"' for app_id, _, _, _, size, _, _ in INTERNAL_GAMES
    )
    sd_apps = "\n".join(
        f'\t\t\t"{app_id}"\t\t"{size}"' for app_id, _, _, _, size, _, _ in SD_GAMES
    )
    # The SD library also lists an app whose manifest is missing -> scanner warning.
    sd_apps += f'\n\t\t\t"{MISSING_MANIFEST_APP_ID}"\t\t"1"'

    library_folders = f'''"libraryfolders"
{{
\t"0"
\t{{
\t\t"path"\t\t"{_vdf_path(internal_lib)}"
\t\t"label"\t\t""
\t\t"contentid"\t\t"8474656447517017716"
\t\t"apps"
\t\t{{
{internal_apps}
\t\t}}
\t}}
\t"1"
\t{{
\t\t"path"\t\t"{_vdf_path(sd_lib)}"
\t\t"label"\t\t"SD Card"
\t\t"apps"
\t\t{{
{sd_apps}
\t\t}}
\t}}
}}
'''
    (steam_root / "steamapps" / "libraryfolders.vdf").write_text(library_folders, encoding="utf-8")

    config_dir = steam_root / "config"
    config_dir.mkdir(parents=True, exist_ok=True)
    (config_dir / "loginusers.vdf").write_text(f'''"users"
{{
\t"{MOST_RECENT_STEAM_ID}"
\t{{
\t\t"AccountName"\t\t"wolftest"
\t\t"PersonaName"\t\t"Wolf"
\t\t"RememberPassword"\t\t"1"
\t\t"MostRecent"\t\t"1"
\t\t"Timestamp"\t\t"1751000000"
\t}}
\t"{OTHER_STEAM_ID}"
\t{{
\t\t"AccountName"\t\t"other"
\t\t"PersonaName"\t\t"Other"
\t\t"MostRecent"\t\t"0"
\t}}
}}
''', encoding="utf-8")

    shared_config_dir = steam_root / "userdata" / str(MOST_RECENT_ACCOUNT_ID) / "7" / "remote"
    shared_config_dir.mkdir(parents=True, exist_ok=True)
    (shared_config_dir / "sharedconfig.vdf").write_text('''"UserRoamingConfigStore"
{
\t"Software"
\t{
\t\t"Valve"
\t\t{
\t\t\t"Steam"
\t\t\t{
\t\t\t\t"apps"
\t\t\t\t{
\t\t\t\t\t"753640"
\t\t\t\t\t{
\t\t\t\t\t\t"tags"
\t\t\t\t\t\t{
\t\t\t\t\t\t\t"0"\t\t"Directly Choice"
\t\t\t\t\t\t\t"1"\t\t"Deck"
\t\t\t\t\t\t}
\t\t\t\t\t}
\t\t\t\t\t"1145360"
\t\t\t\t\t{
\t\t\t\t\t\t"tags"
\t\t\t\t\t\t{
\t\t\t\t\t\t\t"0"\t\t"Deck"
\t\t\t\t\t\t}
\t\t\t\t\t}
\t\t\t\t}
\t\t\t}
\t\t}
\t}
}
''', encoding="utf-8")

    return {
        "steam_root": str(steam_root),
        "internal_lib": str(internal_lib),
        "sd_lib": str(sd_lib),
    }


def _vdf_path(path: Path) -> str:
    # VDF escapes backslashes; emit forward slashes so fixtures work on any OS.
    return str(path).replace("\\", "/")
