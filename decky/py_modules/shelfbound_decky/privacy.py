"""Privacy preview — exactly what would leave the device, shown before any send.

The preview payload contains the REAL snapshot JSON (the byte-for-byte upload body,
pretty-printed) plus a human summary. Nothing is softened or elided: if it's in the
snapshot it's in the preview, and the "never included" list states what the contract
excludes by construction (docs/project/privacy-and-data.md).
"""

from __future__ import annotations

import json

INCLUDED_FACTS = [
    "Steam account id(s) and display names",
    "Steam login name (accountName) — candidate for redaction before upload",
    "Device name, type, OS and best-effort hardware specs",
    "A random, locally persisted device id (not derived from hardware)",
    "Installed games: app id, name, install state, size, timestamps",
    "Library index + label (no filesystem paths)",
    "Your Steam category/collection names",
]

NEVER_INCLUDED = [
    "Full filesystem paths (libraries carry only index + label)",
    "Passwords, credentials, saves, screenshots, arbitrary files",
    "Serial numbers or hardware fingerprints",
    "Which storage a game is on (internal/SD) — shown on-device only",
    "Free-space and mount details — shown on-device only",
]


def build_privacy_preview(snapshot: dict, warnings: list[str]) -> dict:
    accounts = [
        {
            "steamId64": account.get("steamId64"),
            "personaName": account.get("personaName"),
            "accountNameIncluded": "accountName" in account,
        }
        for account in snapshot.get("steamAccounts", [])
    ]

    stats = snapshot.get("stats", {})
    device = snapshot.get("device", {})
    summary = {
        "deviceName": device.get("name"),
        "deviceType": device.get("type"),
        "accounts": accounts,
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
        "snapshotJson": json.dumps(snapshot, indent=2, ensure_ascii=False),
        "warnings": list(warnings),
    }
