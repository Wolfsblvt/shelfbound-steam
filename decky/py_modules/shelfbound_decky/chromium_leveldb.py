"""A read-only reader for a Chromium "Local Storage" LevelDB directory.

Mirrors src/Shelfbound.Steam/Collections/ChromiumLevelDb.cs — enough to fetch the latest
value for one key while the owning process (Steam) holds the LevelDB lock. It parses the
immutable *.ldb SSTables and the *.log write-ahead log *directly* (no LevelDB handle, so
no lock contention), then resolves a key to its highest-sequence value.

Deliberately partial: we don't read the MANIFEST or merge levels. Across all on-disk
files we keep, per user-key, the entry with the greatest sequence number whose type is a
value — skipping deletions. This yields the latest *fully-persisted* value, which is what
we want: while Steam is running it can leave a trailing tombstone whose replacement is
still only in the in-memory memtable (see docs/project/steam-collections.md). Best-effort
by nature.
"""

from __future__ import annotations

import glob
import os
from typing import Iterator, NamedTuple

from . import snappy

_TYPE_VALUE = 1  # 0 = deletion/tombstone

_FOOTER_SIZE = 48  # two block handles (varint pairs, padded) + 8-byte magic
_LOG_BLOCK_SIZE = 32768


class _Entry(NamedTuple):
    user_key: bytes
    sequence: int
    type: int
    value: bytes


def read_latest_value(leveldb_dir: str, user_key: bytes) -> bytes | None:
    """Highest-sequence value stored for the exact user-key across every SSTable and the
    WAL in `leveldb_dir`, or None if no value entry exists (or only tombstones do).
    """
    best: bytes | None = None
    best_seq = 0
    found = False

    for entry in _enumerate_entries(leveldb_dir):
        if entry.type != _TYPE_VALUE:
            continue  # ignore tombstones — keep the latest persisted value
        if entry.user_key != user_key:
            continue
        if not found or entry.sequence > best_seq:
            found = True
            best_seq = entry.sequence
            best = entry.value

    return best


def _enumerate_entries(leveldb_dir: str) -> Iterator[_Entry]:
    # SSTables hold compacted data; the WAL holds the newest writes not yet compacted.
    for path in glob.glob(os.path.join(glob.escape(leveldb_dir), "*.ldb")):
        yield from _safe_read(path, _read_table)
    for path in glob.glob(os.path.join(glob.escape(leveldb_dir), "*.log")):
        yield from _safe_read(path, _read_log)


def _safe_read(path, reader) -> list[_Entry]:
    """A malformed/locked file must never break the scan; it just yields nothing."""
    try:
        return list(reader(path))
    except Exception:  # noqa: BLE001 — any bad/locked file yields nothing, never fatal
        return []


# --- SSTable (.ldb) ------------------------------------------------------------------

def _read_table(path: str) -> Iterator[_Entry]:
    with open(path, "rb") as handle:
        data = handle.read()
    if len(data) < _FOOTER_SIZE:
        return

    # Footer: metaindex handle, index handle (each = varint offset + varint size), magic.
    fp = len(data) - _FOOTER_SIZE
    _, fp = _read_varint(data, fp)  # metaindex offset (unused)
    _, fp = _read_varint(data, fp)  # metaindex size  (unused)
    index_offset, fp = _read_varint(data, fp)
    index_size, fp = _read_varint(data, fp)

    for _key, handle_bytes in _parse_block(_read_block(data, index_offset, index_size)):
        hp = 0
        block_offset, hp = _read_varint(handle_bytes, hp)
        block_size, hp = _read_varint(handle_bytes, hp)

        try:
            block = _read_block(data, block_offset, block_size)
        except Exception:  # noqa: BLE001 — a bad block is skipped, not fatal for the table
            continue

        for internal_key, value in _parse_block(block):
            if len(internal_key) < 8:
                continue
            # Internal key = user-key + 8-byte trailer ((sequence << 8) | type), LE.
            trailer = int.from_bytes(internal_key[-8:], "little")
            yield _Entry(internal_key[:-8], trailer >> 8, trailer & 0xFF, value)


def _read_block(data: bytes, offset: int, size: int) -> bytes:
    """Reads a block by handle, undoing the 1-byte compression-type trailer (snappy/none)."""
    content = data[offset:offset + size]
    compression = data[offset + size]  # trailer: 1 type byte + 4 CRC bytes (CRC unchecked)
    return snappy.decompress(content) if compression == 1 else content


def _parse_block(block: bytes) -> Iterator[tuple[bytes, bytes]]:
    """Parses a LevelDB data/index block: prefix-compressed entries then a restart array."""
    num_restarts = int.from_bytes(block[-4:], "little")
    restart_array = len(block) - 4 - num_restarts * 4

    pos = 0
    last_key = b""
    while pos < restart_array:
        shared, pos = _read_varint(block, pos)
        non_shared, pos = _read_varint(block, pos)
        value_length, pos = _read_varint(block, pos)

        key = last_key[:shared] + block[pos:pos + non_shared]
        pos += non_shared

        value = block[pos:pos + value_length]
        pos += value_length

        last_key = key
        yield key, value


# --- Write-ahead log (.log) ----------------------------------------------------------

def _read_log(path: str) -> Iterator[_Entry]:
    with open(path, "rb") as handle:
        data = handle.read()
    fragment = b""

    pos = 0
    while pos < len(data):
        block_end = min(pos + _LOG_BLOCK_SIZE, len(data))
        bp = pos
        while bp + 7 <= block_end:
            # Record header: 4-byte CRC (unchecked) + 2-byte length + 1-byte type.
            length = data[bp + 4] | (data[bp + 5] << 8)
            record_type = data[bp + 6]
            bp += 7
            if record_type == 0 or bp + length > block_end:
                break  # zero padding to the block boundary, or truncated tail

            chunk = data[bp:bp + length]
            bp += length

            # FULL=1, FIRST=2, MIDDLE=3, LAST=4 — reassemble batches that span records.
            if record_type == 1:
                yield from _parse_batch(chunk)
            elif record_type == 2:
                fragment = chunk
            elif record_type == 3:
                fragment += chunk
            elif record_type == 4:
                yield from _parse_batch(fragment + chunk)
                fragment = b""
        pos += _LOG_BLOCK_SIZE


def _parse_batch(batch: bytes) -> Iterator[_Entry]:
    """Parses a WriteBatch: 8-byte sequence + 4-byte count, then put/delete records."""
    if len(batch) < 12:
        return

    sequence = int.from_bytes(batch[0:8], "little")
    count = int.from_bytes(batch[8:12], "little")

    pos = 12
    i = 0
    while i < count and pos < len(batch):
        record_type = batch[pos]
        pos += 1
        if record_type == _TYPE_VALUE:
            key_length, pos = _read_varint(batch, pos)
            key = batch[pos:pos + key_length]
            pos += key_length
            value_length, pos = _read_varint(batch, pos)
            value = batch[pos:pos + value_length]
            pos += value_length
            # In the WAL the stored key is the user-key directly; the sequence is per-record.
            yield _Entry(key, sequence + i, _TYPE_VALUE, value)
        elif record_type == 0:  # deletion
            key_length, pos = _read_varint(batch, pos)
            key = batch[pos:pos + key_length]
            pos += key_length
            yield _Entry(key, sequence + i, 0, b"")
        else:
            return  # unknown record type — stop parsing this batch
        i += 1


# --- helpers -------------------------------------------------------------------------

def _read_varint(data: bytes, pos: int) -> tuple[int, int]:
    result = 0
    shift = 0
    while True:
        b = data[pos]
        pos += 1
        result |= (b & 0x7F) << shift
        if (b & 0x80) == 0:
            return result, pos
        shift += 7
