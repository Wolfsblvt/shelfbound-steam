"""Privacy-minimized hosted upload projection.

The local snapshot remains the complete portable contract. Hosted upload is a separate,
whitelist-only object graph so a future local field cannot start leaving the device by accident.
``prepare_hosted_upload`` owns canonical serialization; previews and HTTP transport reuse its
exact ``body`` string.
"""

from __future__ import annotations

import json
import socket
from dataclasses import dataclass

from .device_identity import DEFAULT_DEVICE_NAME

PROJECTION_VERSION = "1"

# Mirrors Shelfbound.Client.HostedProjection.FieldPurposes. Paths cover every surviving leaf.
FIELD_PURPOSES: tuple[tuple[str, str], ...] = (
    ("schemaVersion", "Selects the compatible snapshot contract."),
    ("snapshotId", "Identifies this captured snapshot for diagnostics and retry handling."),
    ("createdAt", "Records when the library state was observed."),
    ("source.tool", "Identifies the producer for compatibility diagnostics."),
    ("source.toolVersion", "Identifies producer behavior for compatibility diagnostics."),
    ("source.platform", "Records the producer OS family for compatibility diagnostics."),
    ("device.id", "Random, locally generated device key used to keep device snapshots separate."),
    ("device.name", "User-chosen device label used by token binding, device switching, and curation UI."),
    ("device.type", "Enables device-aware recommendations such as Steam Deck suitability."),
    ("device.os", "Enables OS compatibility and device-aware recommendations."),
    ("device.specs.cpu", "Supports performance-fit recommendations without hardware serials."),
    ("device.specs.logicalCores", "Supports performance-fit recommendations."),
    ("device.specs.totalMemoryBytes", "Supports memory-fit recommendations."),
    ("device.specs.gpu", "Supports graphics-performance recommendations without hardware serials."),
    ("device.specs.osDescription", "Provides only a coarse OS family for compatibility guidance."),
    ("device.specs.architecture", "Supports binary and platform compatibility guidance."),
    ("libraries[].index", "Links installed games to a device library without exposing its path."),
    ("libraries[].label", "Shows the user's library label in storage and curation views."),
    ("libraries[].gameCount", "Provides per-library summary counts."),
    ("libraries[].storage.kind", "Enables internal, SD-card, external, and network storage guidance."),
    ("libraries[].storage.freeBytes", "Enables install-size and free-space guidance."),
    ("libraries[].storage.totalBytes", "Provides storage-capacity context for recommendations."),
    ("games[].appId", "Provides the stable Steam application key used to resolve a game."),
    ("games[].name", "Provides the user's local title, including explicitly added non-Steam titles."),
    ("games[].installed", "Enables installed/backlog filtering per device."),
    ("games[].libraryIndex", "Associates an installed game with its path-free library record."),
    ("games[].installDir", "Provides the relative Steam install-folder name, never a full path."),
    ("games[].sizeOnDiskBytes", "Enables storage-fit and cleanup recommendations."),
    ("games[].playtimeMinutes", "Enables played/unplayed and taste-context recommendations."),
    ("games[].lastUpdated", "Supports freshness and recently-updated views."),
    ("games[].lastPlayed", "Supports recency-aware recommendations."),
    ("games[].categories[]", "Carries the user's Steam collection membership for filtering and taste context."),
    ("categories[].name", "Carries the user's Steam collection vocabulary."),
    ("categories[].gameCount", "Provides collection summary counts."),
    ("stats.libraryCount", "Provides a consistency and summary aggregate."),
    ("stats.installedGameCount", "Provides a consistency and summary aggregate."),
    ("stats.totalSizeOnDiskBytes", "Provides a device storage summary."),
    ("stats.scope", "Distinguishes installed-only scans from complete owned-library scans."),
)


@dataclass(frozen=True)
class HostedUpload:
    """A projected snapshot and the canonical compact JSON used for preview and transport."""

    snapshot: dict
    body: str

    @property
    def game_count(self) -> int:
        return len(self.snapshot["games"])


def prepare_hosted_upload(snapshot: dict) -> HostedUpload:
    """Projects and serializes a local snapshot; raises before any HTTP request on invalid input."""
    projected = project_hosted_snapshot(snapshot)
    body = json.dumps(projected, separators=(",", ":"), ensure_ascii=False)
    return HostedUpload(projected, body)


def project_hosted_snapshot(snapshot: dict) -> dict:
    """Copies only the documented hosted fields from a complete local snapshot."""
    root = _require_dict(snapshot, "snapshot")
    source = _require_dict(_require(root, "source", "snapshot"), "source")
    device = _require_dict(_require(root, "device", "snapshot"), "device")
    stats = _require_dict(_require(root, "stats", "snapshot"), "stats")
    libraries = _require_list(_require(root, "libraries", "snapshot"), "libraries")
    games = _require_list(_require(root, "games", "snapshot"), "games")
    categories = _require_list(_require(root, "categories", "snapshot"), "categories")

    return {
        "schemaVersion": _require_text(root, "schemaVersion", "snapshot"),
        "snapshotId": _require_text(root, "snapshotId", "snapshot"),
        "createdAt": _require(root, "createdAt", "snapshot"),
        "source": {
            "tool": _require_text(source, "tool", "source"),
            "toolVersion": _require_text(source, "toolVersion", "source"),
            "platform": _require_text(source, "platform", "source"),
        },
        "device": _project_device(device),
        "libraries": [_project_library(value) for value in libraries],
        "games": [_project_game(value) for value in games],
        "categories": [_project_category(value) for value in categories],
        "stats": {
            "libraryCount": _require(stats, "libraryCount", "stats"),
            "installedGameCount": _require(stats, "installedGameCount", "stats"),
            "totalSizeOnDiskBytes": _require(stats, "totalSizeOnDiskBytes", "stats"),
            "scope": stats.get("scope", "installedOnly"),
        },
    }


def coarsen_os_description(os_family: str, os_description: str | None) -> str | None:
    """Drops build/kernel detail while retaining the OS family used by recommendations."""
    if not os_description or not os_description.strip():
        return None
    return {
        "windows": "Windows 10/11",
        "linux": "Linux",
        "macOs": "macOS",
    }.get(os_family, "Unknown OS")


def _project_device(device: dict) -> dict:
    os_family = _require_text(device, "os", "device")
    projected = {
        "id": _require_text(device, "id", "device"),
        "name": _project_device_name(device.get("name")),
        "type": _require_text(device, "type", "device"),
        "os": os_family,
    }

    specs_value = device.get("specs")
    if specs_value is not None:
        specs = _require_dict(specs_value, "device.specs")
        projected_specs: dict = {}
        _copy_optional(specs, projected_specs, "cpu")
        _copy_optional(specs, projected_specs, "logicalCores")
        _copy_optional(specs, projected_specs, "totalMemoryBytes")
        _copy_optional(specs, projected_specs, "gpu")
        coarse_os = coarsen_os_description(os_family, specs.get("osDescription"))
        if coarse_os is not None:
            projected_specs["osDescription"] = coarse_os
        _copy_optional(specs, projected_specs, "architecture")
        if projected_specs:
            projected["specs"] = projected_specs

    return projected


def _project_device_name(value: object) -> str:
    if not isinstance(value, str) or not value.strip():
        return DEFAULT_DEVICE_NAME
    try:
        is_hostname = value.strip().casefold() == socket.gethostname().strip().casefold()
    except OSError:
        is_hostname = False
    return DEFAULT_DEVICE_NAME if is_hostname else value.strip()


def _project_library(value: object) -> dict:
    library = _require_dict(value, "libraries[]")
    projected = {
        "index": _require(library, "index", "libraries[]"),
        "label": _require_text(library, "label", "libraries[]"),
        "gameCount": _require(library, "gameCount", "libraries[]"),
    }
    storage_value = library.get("storage")
    if storage_value is not None:
        storage = _require_dict(storage_value, "libraries[].storage")
        projected_storage = {"kind": _require_text(storage, "kind", "libraries[].storage")}
        _copy_optional(storage, projected_storage, "freeBytes")
        _copy_optional(storage, projected_storage, "totalBytes")
        projected["storage"] = projected_storage
    return projected


def _project_game(value: object) -> dict:
    game = _require_dict(value, "games[]")
    projected = {
        "appId": _require(game, "appId", "games[]"),
        "name": _require_text(game, "name", "games[]"),
        "installed": _require(game, "installed", "games[]"),
    }
    for key in (
        "libraryIndex",
        "installDir",
        "sizeOnDiskBytes",
        "playtimeMinutes",
        "lastUpdated",
        "lastPlayed",
    ):
        _copy_optional(game, projected, key)
    projected["categories"] = list(_require_list(game.get("categories", []), "games[].categories"))
    return projected


def _project_category(value: object) -> dict:
    category = _require_dict(value, "categories[]")
    return {
        "name": _require_text(category, "name", "categories[]"),
        "gameCount": _require(category, "gameCount", "categories[]"),
    }


def _copy_optional(source: dict, target: dict, key: str) -> None:
    if key in source and source[key] is not None:
        target[key] = source[key]


def _require(source: dict, key: str, path: str) -> object:
    if key not in source or source[key] is None:
        raise ValueError(f"Hosted upload requires '{path}.{key}'.")
    return source[key]


def _require_text(source: dict, key: str, path: str) -> str:
    value = _require(source, key, path)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"Hosted upload requires non-empty '{path}.{key}'.")
    return value


def _require_dict(value: object, path: str) -> dict:
    if not isinstance(value, dict):
        raise ValueError(f"Hosted upload requires object '{path}'.")
    return value


def _require_list(value: object, path: str) -> list:
    if not isinstance(value, list):
        raise ValueError(f"Hosted upload requires array '{path}'.")
    return value
