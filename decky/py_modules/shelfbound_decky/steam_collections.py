"""Reads a user's *modern* Steam collections (categories) from the desktop client's
Chromium Local Storage LevelDB.

Mirrors src/Shelfbound.Steam/Collections/SteamCollectionsReader.cs. Returns the same
`app id -> ordered category names` shape the legacy sharedconfig.vdf parser produces, so
the scanner can prefer modern collections and fall back to the (often stale)
sharedconfig.vdf. See docs/project/steam-collections.md.

v1 reads only static collections (explicit `added` lists). Dynamic, rule-based
(`filterSpec`) collections are skipped — their membership can't be computed from the
stored data alone. Best-effort: returns None when the store/key is absent so the caller
can fall back. The pure JSON seam is `steam_files.parse_collections_namespace`, unit-
tested without a LevelDB.
"""

from __future__ import annotations

import os

from . import chromium_leveldb, steam_localstorage
from .steam_files import parse_collections_namespace

__all__ = ["try_read", "parse_collections_namespace"]


def try_read(
    account_id: int,
    leveldb_dir: str | None = None,
    steam_root: str | None = None,
) -> dict[int, list[str]] | None:
    """Reads modern collections for `account_id` (the 32-bit Steam account id, i.e. the
    userdata/<id> folder name), or None if the store/key is unavailable. Raises only on a
    genuinely corrupt namespace value, which the caller treats as "fall back to legacy".
    """
    if leveldb_dir is None:
        leveldb_dir = steam_localstorage.locate(steam_root)
    if leveldb_dir is None or not os.path.isdir(leveldb_dir):
        return None

    raw = chromium_leveldb.read_latest_value(leveldb_dir, _build_namespace_key(account_id))
    if not raw:
        return None

    return parse_collections_namespace(_decode_localstorage_value(raw))


def _build_namespace_key(account_id: int) -> bytes:
    """The Chromium LocalStorage key for a user's collections namespace:
    _<origin>\\x00\\x01U<accountId>-cloud-storage-namespace-1.
    """
    prefix = b"_https://steamloopback.host"
    suffix = f"U{account_id}-cloud-storage-namespace-1".encode("ascii")
    return prefix + b"\x00\x01" + suffix


def _decode_localstorage_value(raw: bytes) -> str:
    """Decodes a Chromium LocalStorage value: a 1-byte encoding marker then the payload."""
    # 0x00 = UTF-16LE, 0x01 = Latin-1.
    return raw[1:].decode("utf-16-le") if raw[0] == 0x00 else raw[1:].decode("latin-1")
