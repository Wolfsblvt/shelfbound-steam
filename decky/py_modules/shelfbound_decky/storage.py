"""Per-storage classification for Steam libraries — the Deck-specific signal.

Answers "is this library on the internal SSD, the microSD card, or an external
drive?" from /proc/mounts plus path heuristics. Since contract v0.5.0 this feeds the
snapshot's optional `libraries[].storage` field (via `classify_storage`) as well as
the on-device panel view — the classification runs once, here. Only the kind + free/
total bytes leave the device; the filesystem path never does.

Classification is a pure function over mount-table text so it is testable off-Deck.
[NEEDS-DECK] The heuristics encode expected SteamOS behaviour (SD card = mmcblk
device mounted under /run/media/...) and MUST be verified on real hardware across
SteamOS versions — mount points moved between /run/media/mmcblk0p1 and
/run/media/deck/<label> in past releases.
"""

from __future__ import annotations

import os
import posixpath
from dataclasses import dataclass
from typing import Callable

# Storage kinds — the snapshot contract's `storageKind` vocabulary
# (schema/snapshot.v0.schema.json). The Deck classifier produces internal/sdCard/
# external/network/unknown; the desktop (C#) producer emits the same set.
INTERNAL = "internal"
SD_CARD = "sdCard"
EXTERNAL = "external"
NETWORK = "network"
UNKNOWN = "unknown"

STORAGE_LABELS = {
    INTERNAL: "Internal SSD",
    SD_CARD: "microSD card",
    EXTERNAL: "External drive",
    NETWORK: "Network drive",
    UNKNOWN: "Unknown storage",
}

# Linux network filesystem types — a library on one of these is `network` regardless
# of its backing device name.
NETWORK_FS_TYPES = frozenset({
    "nfs", "nfs4", "cifs", "smb", "smb3", "smbfs", "afs", "ncpfs", "9p", "fuse.sshfs",
})


@dataclass(frozen=True)
class MountEntry:
    device: str
    mount_point: str
    fs_type: str


def parse_proc_mounts(text: str) -> list[MountEntry]:
    """Parses /proc/mounts content (fields are octal-escaped, e.g. \\040 for space)."""
    entries: list[MountEntry] = []
    for line in text.splitlines():
        fields = line.split()
        if len(fields) < 3:
            continue
        entries.append(MountEntry(
            device=_unescape(fields[0]),
            mount_point=_unescape(fields[1]),
            fs_type=fields[2],
        ))
    return entries


def find_mount(path: str, mounts: list[MountEntry]) -> MountEntry | None:
    """The mount entry whose mount point is the deepest ancestor of `path`."""
    best: MountEntry | None = None
    best_depth = -1
    for entry in mounts:
        if _is_within(path, entry.mount_point):
            depth = len([p for p in entry.mount_point.split("/") if p])
            if depth > best_depth:
                best, best_depth = entry, depth
    return best


def classify_block_device(device: str) -> str:
    """Classifies a /dev block device name by its kernel naming convention."""
    name = device.removeprefix("/dev/")
    if name.startswith("mmcblk"):
        return SD_CARD  # SD/eMMC reader — on a Deck this is the microSD slot
    if name.startswith("nvme"):
        return INTERNAL
    if name.startswith("sd"):
        return EXTERNAL  # USB mass storage enumerates as sdX on the Deck
    return UNKNOWN


def classify_library_path(path: str, mounts: list[MountEntry], home: str | None = None) -> str:
    """Storage kind for a Steam library path. Pure; pass mount text + home explicitly in tests."""
    entry = find_mount(path, mounts)
    if entry is not None:
        if entry.fs_type in NETWORK_FS_TYPES:
            return NETWORK
        kind = classify_block_device(entry.device)
        if kind != UNKNOWN:
            return kind
        # Unrecognized device (dm-*, loop, overlay): fall back to the mount location.
        if _is_within(entry.mount_point, "/run/media"):
            return SD_CARD if "mmcblk" in entry.mount_point else EXTERNAL

    resolved_home = home if home is not None else os.path.expanduser("~")
    if resolved_home and _is_within(path, resolved_home):
        return INTERNAL
    if _is_within(path, "/run/media"):
        return SD_CARD if "mmcblk" in path else EXTERNAL
    return UNKNOWN


def read_mounts() -> list[MountEntry]:
    """Reads the live mount table; empty on non-Linux or failure."""
    try:
        with open("/proc/mounts", "r", encoding="utf-8", errors="replace") as handle:
            return parse_proc_mounts(handle.read())
    except OSError:
        return []


def storage_usage(path: str) -> tuple[int, int] | None:
    """(free bytes, total bytes) for the filesystem containing `path`, or None."""
    try:
        stats = os.statvfs(path)  # not available on Windows dev machines
    except (AttributeError, OSError):
        return None
    return (stats.f_bavail * stats.f_frsize, stats.f_blocks * stats.f_frsize)


def classify_storage(
    path: str,
    mounts: list[MountEntry],
    *,
    usage_fn: Callable[[str], tuple[int, int] | None] = storage_usage,
    home: str | None = None,
) -> dict:
    """Builds the contract `libraries[].storage` object for a local library path.

    Returns the storage kind plus best-effort free/total bytes. Sizes are omitted (not
    written as null) when unavailable, matching the snapshot's omit-null convention.
    Never includes the path — kind + sizes only.
    """
    result: dict = {"kind": classify_library_path(path, mounts, home)}
    usage = usage_fn(path)
    if usage is not None:
        result["freeBytes"], result["totalBytes"] = usage
    return result


def _is_within(path: str, ancestor: str) -> bool:
    """Component-wise prefix check on POSIX paths (so /home/deck2 is not inside /home/deck)."""
    path_parts = _parts(path)
    ancestor_parts = _parts(ancestor)
    return path_parts[: len(ancestor_parts)] == ancestor_parts


def _parts(path: str) -> tuple[str, ...]:
    normalized = posixpath.normpath(path.replace("\\", "/"))
    return tuple(part for part in normalized.split("/") if part not in ("", "."))


def _unescape(field: str) -> str:
    """Decodes the octal escapes /proc/mounts uses (\\040 space, \\011 tab, ...)."""
    if "\\" not in field:
        return field
    result: list[str] = []
    i = 0
    while i < len(field):
        char = field[i]
        if char == "\\" and i + 4 <= len(field) and field[i + 1 : i + 4].isdigit():
            try:
                result.append(chr(int(field[i + 1 : i + 4], 8)))
                i += 4
                continue
            except ValueError:
                pass
        result.append(char)
        i += 1
    return "".join(result)
