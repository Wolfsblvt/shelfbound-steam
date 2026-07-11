"""Privacy preview — the exact prepared hosted body, shown before any send."""

from __future__ import annotations

from .hosted_projection import HostedUpload

INCLUDED_FACTS = [
    "User-chosen device label, device type, coarse OS and best-effort hardware specs",
    "A random, locally persisted device id (not derived from hardware)",
    "Installed games: app id, name, install state, size, timestamps",
    "Library index + label (no filesystem paths)",
    "Per-library storage kind (internal/SD/external) and free/total capacity",
    "Your Steam category/collection names",
    "Game names can include private or non-Steam titles you added locally",
]

NEVER_INCLUDED = [
    "Steam account ids, login names, or persona/display names",
    "The machine hostname",
    "Full filesystem paths (libraries carry only index + label)",
    "Passwords, credentials, saves, screenshots, arbitrary files",
    "Serial numbers, MAC addresses, or hardware-derived ids",
    "Mount points, storage device names, or which folder a library lives in",
    "Exact OS build or kernel versions",
]


def build_privacy_preview(upload: HostedUpload, warnings: list[str]) -> dict:
    snapshot = upload.snapshot
    stats = snapshot.get("stats", {})
    device = snapshot.get("device", {})
    summary = {
        "deviceName": device.get("name"),
        "deviceType": device.get("type"),
        "libraryCount": stats.get("libraryCount", 0),
        "gameCount": len(snapshot.get("games", [])),
        "installedGameCount": stats.get("installedGameCount", 0),
        "categoryCount": len(snapshot.get("categories", [])),
        "totalSizeOnDiskBytes": stats.get("totalSizeOnDiskBytes", 0),
        "scope": stats.get("scope", "installedOnly"),
        "included": INCLUDED_FACTS,
        "neverIncluded": NEVER_INCLUDED,
    }

    return {
        "summary": summary,
        "snapshotJson": upload.body,
        "warnings": list(warnings),
    }
