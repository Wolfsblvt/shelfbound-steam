# Shelfbound — Snapshot Schema

The snapshot is Shelfbound's central contract: a versioned, portable, **language-neutral** export of
local library context that connects the scanner, local MCP server, exporters, and future clients
without any of them sharing parsing code. Official hosted clients derive a privacy-minimized subset
from it; the complete local document is not uploaded. See [ARCHITECTURE.md](./ARCHITECTURE.md).

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

`schemaVersion` (semver) is present in every document. Current: **`0.5.0`**.

- **Patch/minor:** additive, backward-compatible (new optional fields). Consumers ignore unknowns.
- **Major:** breaking changes; consumers must branch on the major version.

Pre-1.0 the contract may still move; once hosted ingestion exists, changes go through versioned
migrations. `SnapshotSchema.Version` is the single source of truth in code.

The NuGet package version and JSON schema version are separate identities. The explicit mapping for
the immutable library releases is:

| Library package version | Snapshot schema produced | Notes |
|---|---|---|
| `0.6.0` | `0.4.0` | Historical published payload; immutable, no `libraries[].storage` |
| `0.7.0` | `0.5.0` | Adds optional `libraries[].storage`; current source |

`Directory.Build.props` records the current mapping as `Version` + `SnapshotSchemaVersion`. CI compares
both to the previous `v*` release, package-validates the public API, and inspects the packed nuspec's
version/schema/repository commit. A schema change without both its schema bump and a new package version
fails; a published package version is never overwritten.

## Document shape (v0.5.0)

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | string | semver of this contract |
| `snapshotId` | string | unique per snapshot instance (GUID) |
| `createdAt` | date-time | UTC |
| `source` | object | `tool`, `toolVersion`, `platform` — no user data |
| `device` | object | `id`, `name`, `type`, `os`, optional `specs`; upload-capable producers use a user/neutral label, and hosted projection also neutralizes legacy hostname input |
| `device.specs?` | object | best-effort hardware: `cpu?`, `logicalCores?`, `totalMemoryBytes?`, `gpu?`, `osDescription?`, `architecture?` — no identifiers/serials; exact OS description stays local and is coarsened for hosted upload |
| `steamAccounts[]` | array | local-only identity detail: `steamId64`, `accountName?`, `personaName?`, `mostRecent`; the entire array is omitted from official hosted uploads |
| `libraries[]` | array | `index`, `label`, `gameCount`, optional `storage` — **no filesystem path** |
| `libraries[].storage?` | object | `kind` (`internal`/`sdCard`/`external`/`network`/`unknown`), `freeBytes?`, `totalBytes?` — storage medium + capacity, **no path**. Producers emit `unknown` rather than guess |
| `games[]` | array | see below |
| `categories[]` | array | `name`, `gameCount` — the user's local collections vocabulary |
| `stats` | object | `libraryCount`, `installedGameCount`, `totalSizeOnDiskBytes`, `scope` |
| `stats.scope` | enum | `installedOnly` (default) or `fullLibrary` — whether the game list is the full owned library or only installed games. Absence ≠ non-ownership when `installedOnly`. |

`games[]` entry: `appId`, `name`, `installed`, `libraryIndex?` (null when owned but not installed),
`installDir?` (relative folder name only), `sizeOnDiskBytes?`, `playtimeMinutes?` (from the Steam Web
API), `lastUpdated?`, `lastPlayed?`, `categories[]` (the user's category names for that game, in
Steam's tag order; empty if uncategorized).

Enums: `osPlatform` = `unknown|windows|linux|macOs`; `deviceType` =
`unknown|desktop|laptop|steamDeck|server`; `libraryScope` = `installedOnly|fullLibrary`;
`storageKind` = `internal|sdCard|external|network|unknown`.

## Privacy rules baked into the local contract

These are contract-level guarantees, not just scanner behavior (see
[privacy-and-data.md](./privacy-and-data.md)):

- **No full filesystem paths.** Libraries carry only `index` + `label` (and an optional `storage`
  medium kind + capacity, never a path); games carry only the relative `installDir` name.
- **`device.id` is random and locally persisted** — not derived from hardware or account.
- **No credentials, saves, screenshots, or arbitrary files** are ever represented.
- Steam ids/login/persona names are included for local completeness, so the local file is personal.
  They are not part of the hosted projection.

## Hosted upload projection v1

The hosted body is a producer-side subset; it does **not** change snapshot schema v0.5.0 or the
`/ingest` shape. C# uses `Shelfbound.Client.HostedProjection` (shared by CLI + tray), while Decky's
Python mirror is checked against the same byte-exact golden fixture. Both are whitelist-only and have
an explicit field-purpose manifest.

| Local field/group | Hosted decision |
|---|---|
| `schemaVersion`, `snapshotId`, `createdAt`, `source.*` | Include for compatibility, capture identity/time, and producer provenance |
| `steamAccounts[]` | **Drop entirely**, including `steamId64`, `accountName`, `personaName`, `mostRecent` |
| `device.id` | Include: random, non-hardware id used for multi-device keying |
| `device.name` | Include the user-chosen label; use `Shelfbound device` instead of an automatic hostname, including legacy-hostname input |
| `device.type`, `device.os`, CPU/GPU/cores/RAM/architecture | Include for device-aware and compatibility recommendations |
| `device.specs.osDescription` | Coarsen to `Windows 10/11`, `Linux`, `macOS`, or `Unknown OS`; exact build/kernel is dropped |
| `libraries[]`, `games[]`, `categories[]`, `stats` | Include as the product data; still no full paths/serials/credentials |

Game names and collection names remain personal. `games[].name` can contain a private/non-Steam title
from future or third-party producers even though official producers are Steam-only today. See the
preview/consent details in [privacy-and-data.md](./privacy-and-data.md).

## Scope and what's intentionally missing

The scanner emits **installed Steam games per library**, plus accounts, device info, and the user's
**local categories** (`userdata/<id>/7/remote/sharedconfig.vdf`). With a Steam Web API key it also adds
**owned-but-not-installed games and playtime**. Still to come (each a focused follow-up, tracked in
[PROJECT.md](./PROJECT.md)):

- **Dynamic collections** — the scanner reads the **modern Steam collections** (Chromium Local Storage
  leveldb), falling back to the legacy `sharedconfig.vdf` `tags` store. Static collections (explicit
  membership) are covered; **dynamic, rule-based (`filterSpec`) collections** are not read yet — see
  [steam-collections.md](./steam-collections.md).
- **Owned-not-installed games + playtime** — populated only when a Steam Web API key is provided
  (`--steam-api-key` / `STEAM_WEB_API_KEY`). Without a key, only installed games are listed and
  `stats.scope` stays `installedOnly`; with a key it becomes `fullLibrary`.
- **Non-Steam shortcuts** and deeper per-device install nuance beyond the per-library `storage` kind +
  free/total already in the contract (added additively in v0.5.0; classified per OS by the desktop
  scanner and per mount-table by the decky plugin).

When these land they extend the contract additively and bump the schema version.

## Example (trimmed)

```json
{
  "schemaVersion": "0.5.0",
  "snapshotId": "1b9d…",
  "createdAt": "2026-06-28T10:00:00+00:00",
  "source": { "tool": "shelfbound-cli", "toolVersion": "0.7.0", "platform": "windows" },
  "device": { "id": "b54997ab-…", "name": "Shelfbound device", "type": "unknown", "os": "windows" },
  "steamAccounts": [ { "steamId64": "765611…", "personaName": "…", "mostRecent": true } ],
  "libraries": [
    { "index": 0, "label": "library-0", "gameCount": 11,
      "storage": { "kind": "internal", "freeBytes": 240000000000, "totalBytes": 1000000000000 } }
  ],
  "games": [
    { "appId": 753640, "name": "Outer Wilds", "installed": true,
      "libraryIndex": 1, "installDir": "Outer Wilds", "sizeOnDiskBytes": 11652599956,
      "lastUpdated": "2026-05-01T12:00:00+00:00", "categories": ["Directly Choice"] }
  ],
  "categories": [ { "name": "Directly Choice", "gameCount": 21 }, { "name": "Deck", "gameCount": 18 } ],
  "stats": { "libraryCount": 2, "installedGameCount": 111, "totalSizeOnDiskBytes": 1175000000000,
    "scope": "fullLibrary" }
}
```
