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

`schemaVersion` (semver) is present in every document. Current: **`0.1.0`**.

- **Patch/minor:** additive, backward-compatible (new optional fields). Consumers ignore unknowns.
- **Major:** breaking changes; consumers must branch on the major version.

Pre-1.0 the contract may still move; once hosted ingestion exists, changes go through versioned
migrations. `SnapshotSchema.Version` is the single source of truth in code.

## Document shape (v0.1.0)

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
| `stats` | object | `libraryCount`, `installedGameCount`, `totalSizeOnDiskBytes` |

`games[]` entry: `appId`, `name`, `installed`, `libraryIndex`, `installDir?` (relative folder name
only), `sizeOnDiskBytes?`, `lastUpdated?`, `lastPlayed?`, `categories[]` (reserved; see below).

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

## v0 scope and what's intentionally missing

The v0 scanner emits **installed Steam games per library**, plus accounts and device info. Not yet
populated (each a focused follow-up, tracked in [PROJECT.md](./PROJECT.md)):

- **`games[].categories`** — local Steam collections live in version-dependent formats
  (legacy `sharedconfig.vdf` tags vs modern `localconfig.vdf` `UserCollections`); reserved in the
  contract, currently always `[]`.
- **Owned-but-not-installed games** — require the Steam Web API; not in local files.
- **Per-device install nuance** (Steam Deck internal SSD vs SD card), playtime, non-Steam shortcuts.

When these land they extend the contract additively and bump the schema version.

## Example (trimmed)

```json
{
  "schemaVersion": "0.1.0",
  "snapshotId": "1b9d…",
  "createdAt": "2026-06-28T10:00:00+00:00",
  "source": { "tool": "shelfbound-cli", "toolVersion": "0.1.0", "platform": "windows" },
  "device": { "id": "b54997ab-…", "name": "GERALT", "type": "unknown", "os": "windows" },
  "steamAccounts": [ { "steamId64": "765611…", "personaName": "…", "mostRecent": true } ],
  "libraries": [ { "index": 0, "label": "library-0", "gameCount": 11 } ],
  "games": [
    { "appId": 292030, "name": "The Witcher 3: Wild Hunt", "installed": true,
      "libraryIndex": 1, "installDir": "The Witcher 3", "sizeOnDiskBytes": 63077578235,
      "lastUpdated": "2026-05-01T12:00:00+00:00", "categories": [] }
  ],
  "stats": { "libraryCount": 2, "installedGameCount": 111, "totalSizeOnDiskBytes": 1175000000000 }
}
```
