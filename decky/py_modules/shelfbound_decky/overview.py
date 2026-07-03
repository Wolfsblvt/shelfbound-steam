"""Per-storage overview for the panel UI — internal SSD vs microSD at a glance.

Groups the snapshot's libraries and installed games by the storage kind + free space
the contract now carries (`libraries[].storage`, since v0.5.0) — the classification is
done once, in the snapshot producer, and read here. The grouping, largest-installs, and
labels are on-device presentation composed from that already-uploaded data.
"""

from __future__ import annotations

from . import storage
from .storage import STORAGE_LABELS

_KIND_ORDER = [storage.INTERNAL, storage.SD_CARD, storage.EXTERNAL, storage.NETWORK, storage.UNKNOWN]
_LARGEST_GAMES_SHOWN = 3


def build_storage_overview(snapshot: dict) -> dict:
    """Groups the snapshot's libraries and installed games by storage kind."""
    games_by_library: dict[int, list[dict]] = {}
    for game in snapshot.get("games", []):
        index = game.get("libraryIndex")
        if index is not None:
            games_by_library.setdefault(index, []).append(game)

    groups: dict[str, dict] = {}
    for library in snapshot.get("libraries", []):
        index = library["index"]
        storage_info = library.get("storage") or {}
        kind = storage_info.get("kind", storage.UNKNOWN)

        group = groups.setdefault(kind, {
            "kind": kind,
            "label": STORAGE_LABELS.get(kind, STORAGE_LABELS[storage.UNKNOWN]),
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

        if group["freeBytes"] is None and storage_info.get("freeBytes") is not None:
            group["freeBytes"] = storage_info["freeBytes"]
            group["totalBytes"] = storage_info.get("totalBytes")

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
