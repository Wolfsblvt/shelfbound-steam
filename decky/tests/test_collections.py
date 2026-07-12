"""Modern Steam collections reader — the Python port of the C# oracle
tests/Shelfbound.Steam.Tests/SteamCollectionsReaderTests.cs.

Three layers:
- the pure JSON seam `parse_collections_namespace` (same cases as the C# oracle);
- the key build, LocalStorage value decode, and path locator units;
- an end-to-end read through a hand-built WAL (.log) fixture — the same Chromium format a
  real Steam install writes, so the snappy + leveldb + decode + parse stack is exercised
  without a real capture. Full SSTable (.ldb) + real-capture validation lands with the A1
  hardware pass, exactly how the C# reader was validated end-to-end on a real install.
"""

import json

import pytest

from shelfbound_decky import limits, steam_collections, steam_localstorage
from shelfbound_decky.steam_collections import parse_collections_namespace

# The cloud-storage-namespace-1 value: an array of [entryKey, {value:"<collection-json>"}].
NAMESPACE_JSON = json.dumps([
    ["user-collections.from-tag-Finished",
     {"value": json.dumps({"id": "from-tag-Finished", "name": "Finished",
                           "added": [10, 20], "removed": []})}],
    ["user-collections.uc-dynamic",
     {"value": json.dumps({"id": "uc-dyn", "name": "VR Games", "added": [30],
                           "removed": [], "filterSpec": {"rules": 1}})}],
    ["user-collections.from-tag-Hold",
     {"value": json.dumps({"id": "from-tag-Hold", "name": "Hold", "added": [20]})}],
    ["sc-version", {"value": "3"}],
])

ACCOUNT_ID = 39734273


# --- the pure JSON seam (mirrors the C# oracle) --------------------------------------

def test_parses_static_collections_into_appid_to_categories():
    result = parse_collections_namespace(NAMESPACE_JSON)

    assert result is not None
    assert result[10] == ["Finished"]
    # A game in two collections keeps the order the collections appear in.
    assert result[20] == ["Finished", "Hold"]


def test_skips_dynamic_filterspec_collections():
    result = parse_collections_namespace(NAMESPACE_JSON)

    # appId 30 only lived in a dynamic (filterSpec) collection, so it's absent.
    assert 30 not in result
    assert all("VR Games" not in names for names in result.values())


def test_returns_none_when_no_collections():
    assert parse_collections_namespace('[["sc-version",{"value":"3"}]]') is None
    assert parse_collections_namespace("[]") is None


def test_tolerates_a_malformed_collection_without_dropping_the_rest():
    payload = json.dumps([
        ["user-collections.bad", {"value": "{not valid json"}],
        ["user-collections.ok", {"value": json.dumps({"name": "Done", "added": [7]})}],
    ])
    result = parse_collections_namespace(payload)
    assert result is not None
    assert result[7] == ["Done"]


def test_returns_none_for_non_array_root():
    assert parse_collections_namespace('{"not":"an array"}') is None


def test_raises_on_corrupt_top_level_value():
    # A genuinely corrupt namespace value throws; the caller treats it as "fall back".
    with pytest.raises(ValueError):
        parse_collections_namespace("{not json")


# --- key build + LocalStorage value decode -------------------------------------------

def test_builds_the_localstorage_namespace_key():
    key = steam_collections._build_namespace_key(ACCOUNT_ID)
    assert key == b"_https://steamloopback.host\x00\x01U39734273-cloud-storage-namespace-1"


def test_decodes_latin1_and_utf16_localstorage_values():
    assert steam_collections._decode_localstorage_value(b"\x01" + b"hi") == "hi"
    assert steam_collections._decode_localstorage_value(b"\x00" + "hi".encode("utf-16-le")) == "hi"


# --- path locator (env override + steam-root derivation) -----------------------------

def test_locator_honors_env_override(tmp_path, monkeypatch):
    ls = tmp_path / "ls"
    ls.mkdir()
    monkeypatch.setenv(steam_localstorage.OVERRIDE_ENV_VAR, str(ls))
    assert steam_localstorage.locate() == str(ls)


def test_locator_override_pointing_nowhere_returns_none(tmp_path, monkeypatch):
    monkeypatch.setenv(steam_localstorage.OVERRIDE_ENV_VAR, str(tmp_path / "missing"))
    assert steam_localstorage.locate("ignored-when-override-set") is None


def test_locator_derives_from_steam_root(tmp_path, monkeypatch):
    monkeypatch.delenv(steam_localstorage.OVERRIDE_ENV_VAR, raising=False)
    leveldb = tmp_path / "config" / "htmlcache" / "Local Storage" / "leveldb"
    leveldb.mkdir(parents=True)
    assert steam_localstorage.locate(str(tmp_path)) == str(leveldb)


# --- end-to-end through a WAL (.log) fixture -----------------------------------------

def _varint(value: int) -> bytes:
    out = bytearray()
    while True:
        byte = value & 0x7F
        value >>= 7
        if value:
            out.append(byte | 0x80)
        else:
            out.append(byte)
            return bytes(out)


def _batch(sequence: int, records: list[tuple[int, bytes, bytes]]) -> bytes:
    """A LevelDB WriteBatch: 8-byte sequence + 4-byte count, then put(1)/delete(0) records."""
    batch = bytearray()
    batch += sequence.to_bytes(8, "little")
    batch += len(records).to_bytes(4, "little")
    for rtype, key, value in records:
        batch.append(rtype)
        batch += _varint(len(key)) + key
        if rtype == 1:  # value/put carries a value; deletion does not
            batch += _varint(len(value)) + value
    return bytes(batch)


def _wal(*batches: bytes) -> bytes:
    """Wraps each batch as a FULL log record: 4-byte CRC (unchecked) + 2-byte length + type=1."""
    out = bytearray()
    for batch in batches:
        out += b"\x00\x00\x00\x00" + len(batch).to_bytes(2, "little") + bytes([1]) + batch
    return bytes(out)


def _localstorage_value(namespace_json: str, encoding: str = "latin-1") -> bytes:
    prefix = b"\x00" if encoding == "utf-16-le" else b"\x01"
    return prefix + namespace_json.encode(encoding)


def test_try_read_reads_a_wal_put_end_to_end(tmp_path):
    key = steam_collections._build_namespace_key(ACCOUNT_ID)
    batch = _batch(1000, [(1, key, _localstorage_value(NAMESPACE_JSON))])
    (tmp_path / "000003.log").write_bytes(_wal(batch))

    result = steam_collections.try_read(ACCOUNT_ID, leveldb_dir=str(tmp_path))

    assert result is not None
    assert result[10] == ["Finished"]
    assert result[20] == ["Finished", "Hold"]
    assert 30 not in result  # filterSpec still skipped through the full stack


def test_try_read_skips_a_trailing_tombstone(tmp_path):
    # The documented live-Steam case: the highest-sequence op is a deletion whose rewrite
    # is still only in the memtable, so the latest PERSISTED value must win.
    key = steam_collections._build_namespace_key(ACCOUNT_ID)
    put = _batch(1000, [(1, key, _localstorage_value(NAMESPACE_JSON))])
    delete = _batch(1010, [(0, key, b"")])
    (tmp_path / "000003.log").write_bytes(_wal(put, delete))

    result = steam_collections.try_read(ACCOUNT_ID, leveldb_dir=str(tmp_path))

    assert result is not None
    assert result[10] == ["Finished"]


def test_try_read_decodes_a_utf16_value(tmp_path):
    key = steam_collections._build_namespace_key(ACCOUNT_ID)
    value = _localstorage_value(NAMESPACE_JSON, encoding="utf-16-le")
    (tmp_path / "000003.log").write_bytes(_wal(_batch(1000, [(1, key, value)])))

    result = steam_collections.try_read(ACCOUNT_ID, leveldb_dir=str(tmp_path))

    assert result is not None
    assert result[20] == ["Finished", "Hold"]


def test_try_read_returns_none_when_key_absent(tmp_path):
    other_key = steam_collections._build_namespace_key(ACCOUNT_ID + 1)
    value = _localstorage_value(NAMESPACE_JSON)
    (tmp_path / "000003.log").write_bytes(_wal(_batch(1000, [(1, other_key, value)])))

    # No entry for OUR account id -> None, so the scanner falls back to legacy.
    assert steam_collections.try_read(ACCOUNT_ID, leveldb_dir=str(tmp_path)) is None


def test_try_read_returns_none_when_dir_missing(tmp_path):
    assert steam_collections.try_read(ACCOUNT_ID, leveldb_dir=str(tmp_path / "nope")) is None


def test_try_read_skips_oversized_cache_file(tmp_path):
    path = tmp_path / "000003.log"
    with path.open("wb") as handle:
        handle.truncate(limits.MAX_LEVELDB_FILE_BYTES + 1)

    assert steam_collections.try_read(ACCOUNT_ID, leveldb_dir=str(tmp_path)) is None
