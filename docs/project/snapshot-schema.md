# Shelfbound — Snapshot Schema

The snapshot is Shelfbound's central contract: a versioned, portable, **language-neutral** export of
local library context that connects the scanner, the local MCP server, hosted ingestion, and future
clients without any of them sharing parsing code. See [ARCHITECTURE.md](./ARCHITECTURE.md) for why
this seam matters.

- Machine-readable contract: [`schema/snapshot.v0.schema.json`](../../schema/snapshot.v0.schema.json)
  (JSON Schema 2020-12).
- C# types: `Shelfbound.Core.Model.*`, serialized by `SnapshotSerializer`.

## Conventions

- **camelCase** property names.
- Enums serialize as **camelCase strings** (`windows`, `macOs`, `steamDeck`, …) — never integers.
- Timestamps are **ISO-8601** (`DateTimeOffset`).
- **Null fields are omitted** from output; absent optional field == "unknown/not available".
- Producers should be **strict on output**; consumers should be **lenient** and tolerate unknown
  fields they don't recognize within a major version.

## Versioning

`schemaVersion` (semver) is present in every document. Current: **`0.3.0`**.

- **Patch/minor:** additive, backward-compatible (new optional fields). Consumers ignore unknowns.
- **Major:** breaking changes; consumers must branch on the major version.

Pre-1.0 the contract may still move; once hosted ingestion exists, changes go through versioned
migrations. `SnapshotSchema.Version` is the single source of truth in code.

## Document shape (v0.3.0)

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | string | semver of this contract |
| `snapshotId` | string | unique per snapshot instance (GUID) |
| `createdAt` | date-time | UTC |
| `source` | object | `tool`, `toolVersion`, `platform` — no user data |
| `device` | object | `id`, `name`, `type`, `os` |
| `steamAccounts[]` | array | `steamId64`, `accountName?`, `personaName?`, `mostRecent` |
| `libraries[]` | array | `index`, `label`, `gameCount` — **no filesystem path** |
| `games[]` | array | see below |
| `categories[]` | array | `name`, `gameCount` — the user's local collections vocabulary |
| `stats` | object | `libraryCount`, `installedGameCount`, `totalSizeOnDiskBytes` |

`games[]` entry: `appId`, `name`, `installed`, `libraryIndex?` (null when owned but not installed),
`installDir?` (relative folder name only), `sizeOnDiskBytes?`, `playtimeMinutes?` (from the Steam Web
API), `lastUpdated?`, `lastPlayed?`, `categories[]` (the user's category names for that game, in
Steam's tag order; empty if uncategorized).

Enums: `osPlatform` = `unknown|windows|linux|macOs`; `deviceType` =
`unknown|desktop|laptop|steamDeck|server`.

## Privacy rules baked into the contract

These are contract-level guarantees, not just scanner behavior (see
[privacy-and-data.md](./privacy-and-data.md)):

- **No full filesystem paths.** Libraries carry only `index` + `label`; games carry only the
  relative `installDir` name. This keeps a snapshot safe-by-default even if later uploaded.
- **`device.id` is random and locally persisted** — not derived from hardware or account.
- **No credentials, saves, screenshots, or arbitrary files** are ever represented.
- `accountName` (the Steam login name) is included for local completeness but is a candidate for
  redaction before upload in cloud mode.

## Scope and what's intentionally missing

The scanner emits **installed Steam games per library**, plus accounts, device info, and the user's
**local categories** (`userdata/<id>/7/remote/sharedconfig.vdf`). With a Steam Web API key it also adds
**owned-but-not-installed games and playtime**. Still to come (each a focused follow-up, tracked in
[PROJECT.md](./PROJECT.md)):

- **Modern dynamic collections** — newer Steam "collections" can live in the client's leveldb
  (cloud-storage namespace) rather than `sharedconfig.vdf`. The scanner reads the legacy `tags` store
  (which covers the common case); the leveldb store is not read yet.
- **Owned-not-installed games + playtime** — populated only when a Steam Web API key is provided
  (`--steam-api-key` / `STEAM_WEB_API_KEY`). Without a key, only installed games are listed.
- **Per-device install nuance** (Steam Deck internal SSD vs SD card) and non-Steam shortcuts.

When these land they extend the contract additively and bump the schema version.

## Example (trimmed)

```json
{
  "schemaVersion": "0.3.0",
  "snapshotId": "1b9d…",
  "createdAt": "2026-06-28T10:00:00+00:00",
  "source": { "tool": "shelfbound-cli", "toolVersion": "0.3.0", "platform": "windows" },
  "device": { "id": "b54997ab-…", "name": "GERALT", "type": "unknown", "os": "windows" },
  "steamAccounts": [ { "steamId64": "765611…", "personaName": "…", "mostRecent": true } ],
  "libraries": [ { "index": 0, "label": "library-0", "gameCount": 11 } ],
  "games": [
    { "appId": 753640, "name": "Outer Wilds", "installed": true,
      "libraryIndex": 1, "installDir": "Outer Wilds", "sizeOnDiskBytes": 11652599956,
      "lastUpdated": "2026-05-01T12:00:00+00:00", "categories": ["Directly Choice"] }
  ],
  "categories": [ { "name": "Directly Choice", "gameCount": 21 }, { "name": "Deck", "gameCount": 18 } ],
  "stats": { "libraryCount": 2, "installedGameCount": 111, "totalSizeOnDiskBytes": 1175000000000 }
}
```
