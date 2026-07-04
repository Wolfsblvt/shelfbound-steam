"""Parsers for the individual Steam files Shelfbound reads.

Each function mirrors its C# counterpart in src/Shelfbound.Steam/Steam/ — same fields,
same fallbacks, same ordering — so this producer and the CLI/tray read a Steam install
identically. Pure text-in/data-out; no filesystem access here.
"""

from __future__ import annotations

import json
import posixpath
from dataclasses import dataclass
from datetime import datetime, timezone

from . import vdf
from .vdf import VdfFormatError

# steamId64 = STEAM_ID64_BASE + 32-bit account id (the userdata/<id> folder name).
STEAM_ID64_BASE = 76561197960265728


@dataclass(frozen=True)
class SteamLibraryFolder:
    """A Steam library folder discovered from libraryfolders.vdf."""

    index: int
    path: str
    label: str
    app_ids: tuple[int, ...]

    @property
    def steamapps_path(self) -> str:
        return posixpath.join(self.path, "steamapps")


@dataclass(frozen=True)
class AppManifest:
    """The subset of an appmanifest_*.acf file that Shelfbound cares about."""

    app_id: int
    name: str
    state_flags: int
    install_dir: str | None
    size_on_disk: int | None
    last_updated: datetime | None
    last_played: datetime | None

    @property
    def is_fully_installed(self) -> bool:
        """Steam StateFlag bit 4 (StateFullyInstalled)."""
        return (self.state_flags & 4) == 4


@dataclass(frozen=True)
class SteamAccount:
    """A Steam account discovered in config/loginusers.vdf."""

    steam_id64: str
    account_name: str | None
    persona_name: str | None
    most_recent: bool

    @property
    def account_id(self) -> int | None:
        """32-bit account id (the userdata/<id> folder name), or None if unparsable."""
        try:
            return int(self.steam_id64) - STEAM_ID64_BASE
        except ValueError:
            return None


def parse_library_folders(vdf_text: str) -> list[SteamLibraryFolder]:
    """Parses steamapps/libraryfolders.vdf into the set of Steam library folders."""
    container = vdf.parse(vdf_text).get_object("libraryfolders")
    if container is None:
        raise VdfFormatError("libraryfolders.vdf is missing its 'libraryfolders' root object.")

    folders: list[SteamLibraryFolder] = []
    for key, folder in container.objects.items():
        # Library entries are keyed by a numeric index; skip anything else defensively.
        index = _parse_int(key)
        if index is None:
            continue

        path = folder.get_value("path") or ""
        if not path.strip():
            continue

        app_ids: list[int] = []
        apps = folder.get_object("apps")
        if apps is not None:
            for app_key in apps.values:
                app_id = _parse_int(app_key)
                if app_id is not None:
                    app_ids.append(app_id)

        label = folder.get_value("label") or ""
        folders.append(SteamLibraryFolder(
            index=index,
            path=path,
            label=label if label.strip() else f"library-{index}",
            app_ids=tuple(app_ids),
        ))

    return sorted(folders, key=lambda f: f.index)


def parse_app_manifest(vdf_text: str) -> AppManifest:
    """Parses a single appmanifest_*.acf file."""
    state = vdf.parse(vdf_text).get_object("AppState")
    if state is None:
        raise VdfFormatError("appmanifest is missing its 'AppState' root object.")

    app_id = _parse_int(state.get_value("appid")) or 0
    return AppManifest(
        app_id=app_id,
        name=state.get_value("name") or f"App {app_id}",
        state_flags=_parse_int(state.get_value("StateFlags")) or 0,
        install_dir=state.get_value("installdir"),
        size_on_disk=_parse_int(state.get_value("SizeOnDisk")),
        last_updated=_parse_unix_seconds(state.get_value("LastUpdated")),
        last_played=_parse_unix_seconds(state.get_value("LastPlayed")),
    )


def parse_login_users(vdf_text: str) -> list[SteamAccount]:
    """Parses config/loginusers.vdf into the known Steam accounts (file order preserved)."""
    users = vdf.parse(vdf_text).get_object("users")
    if users is None:
        return []

    return [
        SteamAccount(
            steam_id64=steam_id,
            account_name=user.get_value("AccountName"),
            persona_name=user.get_value("PersonaName"),
            most_recent=user.get_value("MostRecent") == "1",
        )
        for steam_id, user in users.objects.items()
    ]


def parse_shared_config(vdf_text: str) -> dict[int, list[str]]:
    """Parses userdata/<id>/7/remote/sharedconfig.vdf into app id -> ordered category names.

    This is Steam's LEGACY category store — stale for users who manage collections in the
    modern Steam desktop UI. The scanner prefers the modern collections
    (`steam_collections.try_read`) and falls back to this. See the plugin README and
    docs/project/steam-collections.md.
    """
    apps = vdf.parse(vdf_text)
    for key in ("UserRoamingConfigStore", "Software", "Valve", "Steam", "apps"):
        found = apps.get_object(key)
        if found is None:
            return {}
        apps = found

    result: dict[int, list[str]] = {}
    for app_key, app_obj in apps.objects.items():
        app_id = _parse_int(app_key)
        if app_id is None:
            continue

        tags = app_obj.get_object("tags")
        if tags is None or not tags.values:
            continue

        # Tags are keyed by numeric index ("0", "1", ...); preserve that order.
        ordered = sorted(
            tags.values.items(),
            key=lambda item: _parse_int(item[0]) if _parse_int(item[0]) is not None else float("inf"),
        )
        categories = [value for _, value in ordered if value.strip()]
        if categories:
            result[app_id] = categories

    return result


def parse_collections_namespace(json_text: str) -> dict[int, list[str]] | None:
    """Parses the cloud-storage-namespace-1 JSON into app id -> ordered category names.

    Mirrors SteamCollectionsReader.ParseNamespaceJson (the testable seam): the value is an
    array of [entryKey, {value: "<collection-json-string>"}] pairs. v1 reads only STATIC
    collections (explicit `added` lists); dynamic rule-based (`filterSpec`) collections are
    skipped, and a single malformed collection never sinks the rest. Returns None when the
    store holds no usable collections, so the caller falls back to the legacy VDF.

    Raises on a genuinely corrupt top-level value — the caller treats that as "fall back
    to legacy", exactly like the C# reader.
    """
    root = json.loads(json_text)
    if not isinstance(root, list):
        return None

    # Preserve the order collections appear in (Steam's own ordering) for each game.
    by_app: dict[int, list[str]] = {}
    for pair in root:
        if not isinstance(pair, list) or len(pair) != 2:
            continue
        entry_key, entry = pair
        if not isinstance(entry_key, str) or not entry_key.startswith("user-collections."):
            continue
        if not isinstance(entry, dict) or not isinstance(entry.get("value"), str):
            continue
        _add_collection(entry["value"], by_app)

    return by_app or None


def _add_collection(collection_json: str, by_app: dict[int, list[str]]) -> None:
    try:
        collection = json.loads(collection_json)
    except ValueError:
        return  # a single malformed collection shouldn't sink the rest
    if not isinstance(collection, dict):
        return

    # Skip dynamic collections — their membership is rule-based, not the stored 'added' list.
    if collection.get("filterSpec") is not None:
        return

    name = collection.get("name")
    if not isinstance(name, str) or not name.strip():
        return

    added = collection.get("added")
    if not isinstance(added, list):
        return

    for app_id in added:
        # bool is an int subclass in Python; exclude it and non-integers (e.g. floats).
        if not isinstance(app_id, int) or isinstance(app_id, bool):
            continue
        names = by_app.setdefault(app_id, [])
        if name not in names:
            names.append(name)


def _parse_int(text: str | None) -> int | None:
    if text is None:
        return None
    try:
        return int(text)
    except ValueError:
        return None


def _parse_unix_seconds(text: str | None) -> datetime | None:
    seconds = _parse_int(text)
    if seconds is None or seconds <= 0:
        return None
    return datetime.fromtimestamp(seconds, tz=timezone.utc)
