"""Builds a Shelfbound snapshot (contract v0.6.0) from a local Steam installation.

Mirrors the C# SteamScanner (src/Shelfbound.Steam/Steam/SteamScanner.cs): same file
walk, same fallbacks, same warning texts, same output shape. The snapshot dict is
emitted in canonical field order with null/absent fields omitted — byte-compatible in
spirit with SnapshotSerializer's camelCase / omit-null conventions.

Since v0.5.0 each library carries an optional `storage` object (medium kind + free/
total bytes), classified once from the mount table — the same source the on-device
panel reads. Only kind + sizes are emitted; the library path stays local.

Categories are read from the MODERN Steam collections (Chromium leveldb, ported to
stdlib Python in steam_collections.py), falling back to the legacy sharedconfig.vdf —
same prefer-modern/fallback-legacy behaviour as the C# scanner. The one hardware-TBD
seam is the SteamOS Local Storage path (see steam_localstorage.py / A1).

Known prototype divergences from the C# scanner (documented in the plugin README):
- No Steam Web API enrichment (visible not-installed observations + playtime), so `stats.scope`
  is always `installedOnly` and `playtimeMinutes` is never emitted.
"""

from __future__ import annotations

import os
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Callable

from . import SCHEMA_VERSION, TOOL_NAME, steam_collections, storage
from . import limits
from .storage import MountEntry
from .steam_files import (
    SteamAccount,
    SteamLibraryFolder,
    parse_app_manifest,
    parse_library_folders,
    parse_login_users,
    parse_shared_config,
)


@dataclass
class ScanOutput:
    """A produced snapshot plus non-fatal warnings and local-only context.

    `library_paths` (library index -> absolute filesystem path) is a local-only side
    channel — kept for on-device/debug reference and to prove paths never enter the
    snapshot. Storage classification reads the folder path directly during the scan;
    the contract carries only the resulting kind + sizes, never a path.
    """

    snapshot: dict
    warnings: list[str] = field(default_factory=list)
    library_paths: dict[int, str] = field(default_factory=dict)


def build_snapshot(
    steam_root: str,
    device: dict,
    tool_version: str,
    *,
    now: datetime | None = None,
    snapshot_id: str | None = None,
    mounts: list[MountEntry] | None = None,
    usage_fn: Callable[[str], tuple[int, int] | None] | None = None,
    home: str | None = None,
) -> ScanOutput:
    """Scans `steam_root` and assembles the versioned snapshot document.

    `mounts` / `usage_fn` / `home` feed per-library storage classification; they default
    to live reads (`/proc/mounts`, `os.statvfs`, `~`) and are injectable for tests.
    """
    warnings: list[str] = []
    folders = _read_libraries(steam_root, warnings)
    accounts = _read_accounts(steam_root, warnings)
    categories_by_app = _read_categories(steam_root, accounts, warnings)

    resolved_mounts = mounts if mounts is not None else storage.read_mounts()
    resolved_usage = usage_fn if usage_fn is not None else storage.storage_usage

    games: list[dict] = []
    libraries: list[dict] = []
    library_paths: dict[int, str] = {}

    for folder in folders:
        library_paths[folder.index] = folder.path
        count_in_library = 0
        for app_id in folder.app_ids:
            manifest_path = os.path.join(folder.path, "steamapps", f"appmanifest_{app_id}.acf")
            if not os.path.isfile(manifest_path):
                warnings.append(f"Missing manifest for app {app_id} in library {folder.index}.")
                continue

            try:
                manifest = parse_app_manifest(_read_text(manifest_path))
            except Exception as error:  # noqa: BLE001 — any bad manifest is a warning, never fatal
                warnings.append(f"Failed to parse manifest for app {app_id}: {error}")
                continue

            game: dict = {
                "appId": manifest.app_id,
                "name": manifest.name,
                "installed": manifest.is_fully_installed,
                "libraryIndex": folder.index,
            }
            if manifest.install_dir is not None:
                game["installDir"] = manifest.install_dir
            if manifest.size_on_disk is not None:
                game["sizeOnDiskBytes"] = manifest.size_on_disk
            if manifest.last_updated is not None:
                game["lastUpdated"] = manifest.last_updated.isoformat()
            if manifest.last_played is not None:
                game["lastPlayed"] = manifest.last_played.isoformat()
            game["categories"] = list(categories_by_app.get(manifest.app_id, []))

            games.append(game)
            count_in_library += 1

        libraries.append({
            "index": folder.index,
            "label": folder.label,
            "gameCount": count_in_library,
            # Storage medium + free/total, classified from the (local-only) path. Kind +
            # sizes only reach the contract; the path never does.
            "storage": storage.classify_storage(
                folder.path, resolved_mounts, usage_fn=resolved_usage, home=home
            ),
        })

    created_at = now if now is not None else datetime.now(timezone.utc)
    snapshot = {
        "schemaVersion": SCHEMA_VERSION,
        "snapshotId": snapshot_id or str(uuid.uuid4()),
        "createdAt": created_at.isoformat(),
        "source": {
            "tool": TOOL_NAME,
            "toolVersion": tool_version,
            "platform": device["os"],
        },
        "device": device,
        "steamAccounts": [_account_dict(account) for account in accounts],
        "libraries": libraries,
        "games": games,
        "categories": _summarize_categories(categories_by_app),
        "stats": {
            "libraryCount": len(libraries),
            "installedGameCount": sum(1 for game in games if game["installed"]),
            "totalSizeOnDiskBytes": sum(game.get("sizeOnDiskBytes") or 0 for game in games),
            # No Steam Web API enrichment in this producer — the game list is
            # installed-only and absence must never imply non-ownership.
            "scope": "installedOnly",
        },
    }

    return ScanOutput(snapshot=snapshot, warnings=warnings, library_paths=library_paths)


def _read_libraries(steam_root: str, warnings: list[str]) -> list[SteamLibraryFolder]:
    library_folders_path = os.path.join(steam_root, "steamapps", "libraryfolders.vdf")
    if os.path.isfile(library_folders_path):
        return parse_library_folders(_read_text(library_folders_path))

    warnings.append(
        f"libraryfolders.vdf not found at '{library_folders_path}'; using the primary library only."
    )
    return [SteamLibraryFolder(index=0, path=steam_root, label="library-0", app_ids=())]


def _read_accounts(steam_root: str, warnings: list[str]) -> list[SteamAccount]:
    login_users = os.path.join(steam_root, "config", "loginusers.vdf")
    if not os.path.isfile(login_users):
        warnings.append("config/loginusers.vdf not found; no Steam accounts recorded.")
        return []

    try:
        return parse_login_users(_read_text(login_users))
    except Exception as error:  # noqa: BLE001
        warnings.append(f"Failed to parse loginusers.vdf: {error}")
        return []


def _read_categories(
    steam_root: str, accounts: list[SteamAccount], warnings: list[str]
) -> dict[int, list[str]]:
    """Reads local categories for the most-recent account that has them.

    Mirrors the C# SteamScanner.ReadCategories: prefer the MODERN Steam collections
    (Chromium leveldb, via steam_collections.try_read) and fall back to the legacy
    sharedconfig.vdf — stale for users who manage collections in the modern Steam UI. The
    fallback warning fires only when we actually use the legacy file, so a successful
    modern read is silent. See docs/project/steam-collections.md.
    """
    ordered = sorted(accounts, key=lambda account: 0 if account.most_recent else 1)
    for account in ordered:
        account_id = account.account_id
        if account_id is None:
            continue

        # Prefer modern collections; a non-empty result wins outright.
        try:
            modern = steam_collections.try_read(account_id, steam_root=steam_root)
            if modern:
                return modern
        except Exception as error:  # noqa: BLE001 — a bad modern read just falls back to legacy
            warnings.append(f"Failed to read modern collections for account {account_id}: {error}")

        # Fall back to the legacy sharedconfig.vdf.
        path = os.path.join(steam_root, "userdata", str(account_id), "7", "remote", "sharedconfig.vdf")
        if not os.path.isfile(path):
            continue

        try:
            legacy = parse_shared_config(_read_text(path))
        except Exception as error:  # noqa: BLE001
            warnings.append(f"Failed to parse sharedconfig.vdf for account {account_id}: {error}")
            continue

        warnings.append(
            f"Modern Steam collections were unavailable for account {account_id}; categories "
            "came from the legacy sharedconfig.vdf and may be stale."
        )
        return legacy

    if accounts:
        warnings.append("No categories found (modern collections or sharedconfig.vdf).")
    return {}


def _account_dict(account: SteamAccount) -> dict:
    result: dict = {"steamId64": account.steam_id64}
    if account.account_name is not None:
        result["accountName"] = account.account_name
    if account.persona_name is not None:
        result["personaName"] = account.persona_name
    result["mostRecent"] = account.most_recent
    return result


def _summarize_categories(categories_by_app: dict[int, list[str]]) -> list[dict]:
    counts: dict[str, int] = {}
    for categories in categories_by_app.values():
        for name in categories:
            counts[name] = counts.get(name, 0) + 1

    # Count desc, then name asc (codepoint order — matches C# StringComparer.Ordinal).
    ordered = sorted(counts.items(), key=lambda item: (-item[1], item[0]))
    return [{"name": name, "gameCount": count} for name, count in ordered]


def _read_text(path: str) -> str:
    if os.path.getsize(path) > limits.MAX_VDF_FILE_BYTES:
        raise ValueError(f"Steam text file exceeds the {limits.MAX_VDF_FILE_BYTES}-byte limit.")
    with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
        return handle.read()
