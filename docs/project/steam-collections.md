# Reading modern Steam collections (categories) — design

> **Status: approved; implementation in progress.** Confirmed bug + validated approach. Decision logged
> in [DECISIONS.md](./DECISIONS.md): hand-rolled, dependency-free reader with a legacy fallback.

## The bug

The scanner reads per-game categories from the **legacy** `userdata/<id>/7/remote/sharedconfig.vdf`
`tags` store. For anyone who manages collections in the **modern Steam desktop UI**, that file is
**stale** — Steam no longer keeps it in sync. We therefore emit *wrong* categories, which an AI then
states as fact.

Confirmed on the maintainer's real install:

| Game | Legacy `sharedconfig.vdf` (what we read) | Modern collections (what the user sees) |
|---|---|---|
| Slay the Princess (appid `1989270`) | `Currently`, `Directly Choice` ❌ | `Finished` ✅ |

The legacy store had ~11 sparse categories; the modern store has 24 (e.g. `Finished`=78 games,
`Deck Choice`, `Hold`, …). The legacy data is years out of date.

## Where modern collections actually live

Not under the Steam **install** dir. They're in the Steam client's Chromium **Local Storage LevelDB**:

- **Windows:** `%LOCALAPPDATA%\Steam\htmlcache\Local Storage\leveldb\` (`*.ldb`, `*.log`, `MANIFEST`, …)
- Linux/macOS: under Steam's `htmlcache` equivalent (path TBD; Windows is the validated path).

The relevant entry is a Chromium LocalStorage value:

- **origin:** `https://steamloopback.host`
- **key bytes:** `_https://steamloopback.host\x00\x01U<accountId>-cloud-storage-namespace-1`
  (`<accountId>` is the 32-bit Steam account id = the `userdata/<id>` folder name)
- **value:** a 1-byte encoding prefix (`0x01` Latin-1 / `0x00` UTF-16LE) then a JSON array.

The JSON array is `[[entryKey, {key,timestamp,value,version}], …]`. Collection entries have
`entryKey` starting `user-collections.`; their `value` is a JSON string:

```json
{"id":"from-tag-Finished","name":"Finished","added":[400,620,...],"removed":[],"filterSpec":null}
```

- `added` / `removed` are appid lists (the explicit membership).
- `filterSpec != null` marks a **dynamic** collection (rule-based, e.g. "all VR games"); its membership
  is computed from rules, not a static list — handle separately or skip in v1.
- Built-ins: `favorite` → "Favorites", `hidden` → "Hidden".

## Why this is fragile (the part that needs a decision)

Reading a Chromium LevelDB correctly — **while Steam is running and holding the lock** — means parsing
the immutable files directly. Naively "read all `.ldb`, last write wins" returns **wrong** data. The
real mechanics, all confirmed on disk:

1. **Internal keys.** Each stored key = userkey + an 8-byte trailer (`(sequence<<8)|type`,
   little-endian; `type` 1=value, 0=deletion). Must strip it and use the **sequence** to order versions.
2. **Snappy.** Data blocks are per-block snappy-compressed (decompression only — ~70 lines, validated).
3. **SSTable format.** Footer (48 bytes) → index block → data blocks (restart arrays + shared-prefix
   keys). The index gives each data block's handle; iterate all data blocks.
4. **WAL (`.log`).** The newest writes live in the write-ahead log (32 KB blocks → records → write
   batches) before compaction — higher sequence numbers than the SSTables.
5. **Tombstones + Steam-running inconsistency.** On the maintainer's live install the *highest*-sequence
   op for the namespace key was a **deletion** (seq 10260) with **no rewrite on disk** — the real value
   was the highest-sequence **value** entry (seq 10253, the 24-collection set). The re-put was still in
   Steam's in-memory memtable, not flushed. So the only sane on-disk read is **"latest fully-persisted
   value, skipping a trailing tombstone"** — a heuristic, not guaranteed-correct, and inherently a bit
   behind the live client when Steam is mid-edit.

### Validated algorithm (implementation-ready)

```
for each *.ldb (SSTable) and *.log (WAL) in the leveldb dir:
    collect (userkey, sequence, type, value) for every entry
per userkey: keep the entry with the highest sequence whose type == value (skip tombstones)
find userkey == _<origin>\x00\x01U<accountId>-cloud-storage-namespace-1
decode value (1-byte enc prefix) -> JSON -> collections
build appid -> [category names]   (same shape SharedConfigParser already returns)
```

A Python port of exactly this produced the correct result (Slay the Princess → `Finished`), so the C#
port is mechanical. MANIFEST parsing (to filter obsolete tables) was **not** needed — highest-sequence
-value-per-userkey across all files already resolves correctly, because obsolete duplicates carry lower
sequences.

## Chosen approach

**Hand-rolled, dependency-free reader** (~350 lines: snappy + SSTable + WAL + Chromium decode +
collections parse), isolated under `Shelfbound.Steam.Collections`, **with the legacy `sharedconfig.vdf`
as a fallback** so a failed/empty modern read is exactly today's behaviour — never worse. Maintained
best-effort across Steam updates. (Rejected: a native LevelDB/RocksDB dependency — too heavy and
per-RID for a cross-platform open-core lib, and it still needs a copy-and-open-read-only dance to dodge
the lock; and "do nothing", since categories are a headline feature and currently *wrong* for modern-UI
users.) See [DECISIONS.md](./DECISIONS.md).

### Caveats shipped with it

- **Windows-solid, best-effort elsewhere** (validated on Windows; Linux/macOS paths to confirm).
- **Steam-running freshness:** can lag the live client by the last unflushed edit; acceptable, documented.
- **Dynamic (`filterSpec`) collections:** v1 reads static `added`/`removed` only; rule-based ones are a
  follow-up.
- **Privacy/tests:** the real leveldb holds the user's library; tests use synthetic fixtures + the pure
  pieces (snappy vectors, Chromium-key decode, collections-JSON parse). End-to-end is validated against
  the local install, not a committed fixture.
