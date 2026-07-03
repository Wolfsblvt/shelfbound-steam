# Shelfbound — Architecture (open-source core)

How the core is built and why the boundaries are where they are. Code is the source of truth for
*how*; this file captures structure, seams, and invariants.

## Stack

- **.NET (C#), targeting `net10.0`** for everything the maintainer controls — local core, CLI, tray
  app, and the local MCP server. Chosen for development speed in the maintainer's primary language and a
  **first-class official MCP SDK** (`ModelContextProtocol`, v1.0+, maintained with Microsoft).
- Other languages only where a platform forces them: the **Decky plugin** (Steam Deck) is
  Python + React/TS by Decky's design — a hardware-gated prototype lives in `decky/`. Such clients
  interoperate through the **snapshot JSON contract**, not by sharing C# code.

## The keystone: the snapshot contract

The single most important architectural decision. Every producer and consumer of library context
talks through one **versioned, language-neutral snapshot**, never through each other's internals:

```
Steam local files ─┐
Steam Web API ─────┼─► scanner/exporter ─► Shelfbound snapshot (versioned JSON) ─┬─► local MCP server
device/env ────────┘                                                            ├─► export / upload
                                                                                └─► future clients (Decky, …)
```

Consequences:
- The local MCP server, the tray app, a future Decky plugin, and any external consumer **do not
  duplicate** Steam-file parsing or the data model.
- The contract is JSON Schema (`schema/snapshot.v0.schema.json`) plus C# types in `Shelfbound.Core`.
  A non-.NET client can emit a valid snapshot without referencing our assemblies.
- Schema is **semver-versioned**; `schemaVersion` travels in every document. See
  [snapshot-schema.md](./snapshot-schema.md).

## Repository boundary (public core vs private product)

Two repositories, separated from day one — because *public is forever*, including git history.

- **`shelfbound-steam` (this repo, public, open source)** — the free local/core capability users can
  run and audit: domain models + snapshot contract, the local Steam scanner, the query/search engine,
  the CLI, the tray app, and the local MCP server. Licensed **AGPL-3.0-or-later** (whole repo).
- **A separate, private product** — a proprietary repository, out of scope here. It interoperates with
  the core **through the snapshot contract** (the published JSON format), never by embedding core code.

No product/hosted code belongs in this repo.

## Project layout (this repo)

```
src/
  Shelfbound.Core      Domain models + snapshot contract + serializer. Pure, no I/O.
  Shelfbound.Steam     Local Steam scanner (VDF/ACF, modern + legacy categories, device specs) +
                       Steam Web API client + enrichment. The differentiator; auditable and open.
  Shelfbound.Query     Merged library view (snapshot facts + user-data) + deterministic
                       filter/sort/summary + recommendations. No I/O, no LLM; reused by the MCP server,
                       the tray, and the hosted layer.
  Shelfbound.Storage   Local config (API key), the identity seam, and the user-data store
                       (statuses/ratings/completion/aspects, scoped memories, category meanings).
  Shelfbound.Client    Shared scan-to-snapshot builder + Shelfbound-server client, reused by the CLI
                       and the tray.
  Shelfbound.Cli       `shelfbound` CLI — setup, scan (+ enrichment), profile, upload.
  Shelfbound.Tray      Cross-platform tray agent (Avalonia): background sync, status, account connect.
  Shelfbound.Mcp       `shelfbound-mcp` — local MCP server (stdio): read + write/remember tools.
tests/
  Shelfbound.Steam.Tests           xUnit + Shouldly. Parsers, scanner, query engine, enricher,
                                   snappy/leveldb collections reader.
schema/
  snapshot.v0.schema.json          The language-neutral contract.
decky/
  Steam Deck (Decky) plugin prototype — Python backend + React/TS panel emitting the same
  snapshot contract. Own toolchain (pnpm/rollup + stdlib Python), hardware-gated; see decky/README.md.
```

The whole repo is **AGPL-3.0-or-later** (single license; see [DECISIONS.md](./DECISIONS.md) and the
README license section).

Boundaries / invariants:
- `Shelfbound.Core` has **no file/network/environment access** — it stays a clean contract that any
  consumer can interoperate through. Anything that touches the local machine lives in
  `Shelfbound.Steam` (Steam files) or `Shelfbound.Cli` (device identity, args, output).
- The scanner takes a resolved `SnapshotDevice` and a Steam root path as input; it does not reach
  into global state. This keeps it testable against a temp fixture tree.

## Identity, config & user-data storage

Three concerns kept separate so the hosted layer can swap implementations without touching the data
model (`Shelfbound.Storage`):

- **Config** (`ShelfboundConfig`, `ShelfboundPaths`) — the Steam Web API key and active profile, in the
  user's config dir (never the repo). The CLI `setup` command writes it; the CLI and MCP both read it.
- **Identity seam** (`ProfileIdentity`) — resolves the current **owner/profile id**. Locally this is
  the configured profile, else the primary Steam account, else `local`. There is no local login —
  your machine is your identity. The hosted layer swaps in an auth-backed resolver and **the storage
  contract and data model don't change.**
- **User-data store** (`IUserDataStore`, local `JsonUserDataStore`) — per-owner durable data: per-game
  status/rating/completion/aspects, scoped **memories** (global/game/category, with
  source/evidence/confidence), and category meanings. One JSON file per owner, atomic writes, shared
  consistently by the CLI and the MCP server. A database backend (SQLite, then the hosted DB)
  implements the same interface when concurrency/scale demands.

This is the **derived/user data** category — deliberately separate from the raw snapshot (see
[privacy-and-data.md](./privacy-and-data.md)).

## Scanner internals (current)

1. **Locate Steam** (`SteamInstallLocator`): explicit override → `SHELFBOUND_STEAM_PATH` → per-OS
   well-known paths (Windows / macOS / Linux+SteamOS). Windows registry lookup is a planned add.
2. **Parse `libraryfolders.vdf`** → libraries (index, path, installed app ids).
3. For each app id, **parse `appmanifest_<id>.acf`** → name, install state (StateFlags bit 4),
   install dir, size, last updated/played.
4. **Parse `config/loginusers.vdf`** → Steam accounts.
5. **Read local categories** (most-recent account, app id → ordered category names): the **modern
   Steam collections** from the desktop client's Chromium leveldb (`Shelfbound.Steam.Collections` — a
   hand-rolled snappy + LevelDB reader), falling back to the legacy
   `userdata/<id>/7/remote/sharedconfig.vdf` when unavailable. See [steam-collections.md](./steam-collections.md).
6. **Collect device specs** (best-effort CPU/RAM/GPU/OS via `Shelfbound.Steam.HardwareInfo`).
7. **Assemble** a `SnapshotDocument` with device (+ specs), accounts, libraries, games (with their
   categories), a category summary, and stats (including the library scope: installed-only vs full).

Non-fatal problems become `warnings` on the result rather than aborting the scan.

## Local vs external boundary

- **Local mode:** scanner → snapshot on disk → local MCP server reads it (plus a local user-data
  store for notes/profile). Nothing leaves the machine. Open source, auditable.
- **External consumers** (a separate hosted product, a future Decky plugin, anything else): receive
  the same snapshot and interoperate **only through the contract**. Their internals are out of scope
  here.

The snapshot contract is the single boundary; the open core makes no assumptions about who consumes it.

## Data model concepts (core/local)

Keep four data categories **separate and traceable** — never one untraceable blob:

1. **Raw local data** read from the machine (snapshot inputs).
2. **Uploaded/exported snapshot data** (what the user chose to share).
3. **External metadata** (Steam API, completion times, tags) — clearly third-party.
4. **Derived / AI-generated data** (taste signals, summaries) — carries source/evidence/confidence.

Core/local entities introduced incrementally as features land: `SteamAccount`, `Device`, `Snapshot`,
`Game/App`, `LibraryGame`, `Category` + `CategoryDefinition`, `InstalledState`, `GameStatus`,
`GameOpinion`, `GameNote`, `UserPreference`, `TasteProfile`. (Hosted/product entities are defined
privately, not here.)

Enumerable states worth fixing early (centralized, not loose strings):
- **GameStatus:** unknown, want_to_play, playing, paused, finished, dropped, replayable,
  comfort_game, ignored, played_elsewhere.
- **Opinion:** loved, liked, mixed, disliked, never_again, skip/unknown.
- **Taste signals:** liked/disliked aspects, moods, themes, genres, mechanics, preferred lengths,
  avoided tags, evidence games, confidence, source.

The snapshot (`Shelfbound.Core`) currently models only what the local scanner produces. User-owned
data (notes/status/opinions/profile) attaches in later phases via the local MCP user store — not the
raw snapshot.

## Storage strategy

- **Local:** snapshot JSON file(s) + a local user-data store (format TBD when the local MCP server
  lands — likely a small embedded store or JSON files under the user config dir).
- **Hosted:** handled privately by the product layer.

## Build order

The local-first core is built: scanner, local categories (modern + legacy), owned-not-installed +
playtime, the query/recommendation engine, the user-data store, the tray app, and the local MCP server.
Remaining local work and the forward roadmap live in [PROJECT.md](./PROJECT.md); the local-first
rationale is in [DECISIONS.md](./DECISIONS.md). Hosted/product phases are tracked privately.
