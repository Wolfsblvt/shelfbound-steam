"""Per-storage overview for the panel UI — internal SSD vs microSD at a glance.

Composes the snapshot with local-only context (library paths, mount table, free
space) into the view the Deck panel shows. Everything here is UI-only: none of it is
part of the snapshot or ever uploaded.
"""

from __future__ import annotations

from typing import Callable

from . import storage
from .storage import STORAGE_LABELS, MountEntry

_KIND_ORDER = [storage.INTERNAL, storage.SD_CARD, storage.EXTERNAL, storage.UNKNOWN]
_LARGEST_GAMES_SHOWN = 3


def build_storage_overview(
    snapshot: dict,
    library_paths: dict[int, str],
    mounts: list[MountEntry],
    *,
    usage_fn: Callable[[str], tuple[int, int] | None] = storage.storage_usage,
    home: str | None = None,
) -> dict:
    """Groups the snapshot's libraries and installed games by storage kind."""
    games_by_library: dict[int, list[dict]] = {}
    for game in snapshot.get("games", []):
        index = game.get("libraryIndex")
        if index is not None:
            games_by_library.setdefault(index, []).append(game)

    groups: dict[str, dict] = {}
    for library in snapshot.get("libraries", []):
        index = library["index"]
        path = library_paths.get(index)
        kind = storage.classify_library_path(path, mounts, home) if path else storage.UNKNOWN

        group = groups.setdefault(kind, {
            "kind": kind,
            "label": STORAGE_LABELS[kind],
            "libraries": [],
            "gameCount": 0,
            "installedGameCount": 0,
            "sizeOnDiskBytes": 0,
            "freeBytes": None,
            "totalBytes": None,
            "games": [],
        })
        group["libraries"].append({
            "index": index,
            "label": library["label"],
            "gameCount": library["gameCount"],
        })
        games = games_by_library.get(index, [])
        group["gameCount"] += len(games)
        group["installedGameCount"] += sum(1 for game in games if game.get("installed"))
        group["sizeOnDiskBytes"] += sum(game.get("sizeOnDiskBytes") or 0 for game in games)
        group["games"].extend(games)

        if group["freeBytes"] is None and path:
            usage = usage_fn(path)
            if usage is not None:
                group["freeBytes"], group["totalBytes"] = usage

    storages = []
    for kind in _KIND_ORDER:
        group = groups.get(kind)
        if group is None:
            continue
        games = group.pop("games")
        group["largestGames"] = [
            {
                "appId": game["appId"],
                "name": game["name"],
                "sizeOnDiskBytes": game.get("sizeOnDiskBytes") or 0,
                "installed": game.get("installed", False),
            }
            for game in sorted(games, key=lambda g: g.get("sizeOnDiskBytes") or 0, reverse=True)[:_LARGEST_GAMES_SHOWN]
        ]
        storages.append(group)

    return {"storages": storages}
