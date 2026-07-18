# Shelfbound — Project Overview (open-source core)

> Status as of 2026-07-15. This file is the living overview of the **open-source core**. Read it first.

## What Shelfbound is

Shelfbound is a **personal gaming context layer** that makes a person's *real* Steam library —
installed games per device, local categories and their personal meaning, notes, statuses, and taste —
available to AI tools through [MCP](https://modelcontextprotocol.io).

Short version: **Shelfbound makes your real Steam library AI-readable.**

It is an **unofficial** tool for Steam, not affiliated with or endorsed by Valve. "Steam" is used
descriptively.

This repository (`shelfbound-steam`) is the **free, open-source core**: the local Steam scanner, the
snapshot contract, the CLI, and (soon) a local MCP server. It is licensed **AGPL-3.0-or-later** and is
intended to **stay free**. A separate hosted version may be offered for convenience, developed
privately and out of scope for this repo. See [DECISIONS.md](./DECISIONS.md) for the open-core
rationale.

## Why it exists (the differentiator)

A plain Steam Web API wrapper is not interesting — anyone can expose visibility-gated game observations and playtime. The
unique value is what the public API *cannot* give, read from the **local Steam client**:

- local categories / collections and **what they personally mean** (`Soon`, `Deck`, `Paused`, …)
- installed state **per device** (desktop vs laptop vs Steam Deck; internal SSD vs SD card)
- the user's notes and statuses (finished / paused / dropped / comfort replay / played elsewhere)
- a durable personal **taste profile** built from explicit opinions

Give an AI those structured facts through MCP and it can finally reason about *your* library:

> "What installed games from my `Soon` category are short?"
> "What should I play on my Steam Deck tonight?"
> "Mark Outer Wilds finished — I played it on Game Pass — and don't recommend it as backlog."

The local Steam data is the moat. AI reasoning is commodity; good structured facts are not.

## Goals (for the core)

- Be a reliable, privacy-conscious **data layer**; reasoning lives in the AI client.
- **Local-first and auditable**: run everything locally and see exactly what is read. Nothing leaves
  the machine unless the user explicitly chooses to.
- One **versioned snapshot contract** shared by every producer/consumer — no duplicated parsing.

## Non-goals

- No social/review platform, browser extension, or mobile app. No "support every platform" first.
- No heavy server-side AI and no scraping dependency in the core.
- Not an Augmented Steam / SteamDB / Backloggd / Playnite / IsThereAnyDeal clone.
- The separate hosted product is **not** part of this repo.

## Current status

**Implemented (local core):**
- Snapshot contract `v0.6.0` (`schema/snapshot.v0.schema.json`, models in `Shelfbound.Core`) — retains
  optional per-library `storage` and adds honest `observedSubset` coverage. The immutable library
  package mapping is `0.8.0` → schema `0.6.0`; published `0.7.0` remains schema `0.5.0` and `0.6.0`
  remains schema `0.4.0`.
- Local Steam scanner (`Shelfbound.Steam`): explicit/environment/Windows-current-user-registry/default install discovery,
  `libraryfolders.vdf`,
  `appmanifest_*.acf`, `loginusers.vdf`, **local categories** — modern Steam collections (a hand-rolled
  Chromium-leveldb reader) with the legacy `sharedconfig.vdf` as fallback — and a minimal VDF parser.
- **Steam Web API** client + enrichment: positive visible owned-game/playtime observations (with an
  API key), including visible not-installed rows. The snapshot carries `installedOnly`,
  `observedSubset`, or explicitly complete `fullLibrary` scope; current Web API output is always partial,
  and CLI/MCP wording keeps absence from becoming a non-ownership claim.
- **`Shelfbound.Query`**: deterministic filter/sort/summary over a **merged library view** (snapshot
  facts + user-data) — search by category, install state, playtime, **status, rating, and completion**;
  **recency** as human phrases (installed/last-played + "added N ago", inferred from first-seen). The
  additive **QueryPlan v1** contract, text parser/canonical serializer, capability validator, structured
  diagnostics, and public conformance corpus are built beside `LibraryFilter`; the existing engine and local
  MCP behavior have not migrated to that plan yet. See [query-plan.md](./query-plan.md).
- **Local MCP server** (`Shelfbound.Mcp`, stdio): read tools (`search_library`, `get_library_summary`,
  `get_categories`, `get_game_details`, `find_installed_unplayed`), write/remember tools
  (`record_game_status`, `record_game_opinion`, `set_game_completion`, `set_category_definition`,
  `remember`, `delete_memory`, `get_game_user_data`, `get_remembered`), an onboarding endpoint
  (`get_profile_status`), and **server instructions** that push the model to save context and onboard.
- **`Shelfbound.Storage`**: local config (API key), the **identity seam** (owner/profile), and a
  user-data store — per-game status/rating/completion/aspects, scoped memories, category meanings, and
  conservative **first-seen** tracking (`added` only under stable, actually complete coverage) —
  shared by the CLI and MCP server.
- CLI (`Shelfbound.Cli`): `shelfbound setup` (API key), `shelfbound scan` (+ enrichment),
  `shelfbound profile` (a local "what Shelfbound remembers" view), and privacy-minimized hosted
  upload with an exact `--dry-run` preview.
- **Tray agent** (`Shelfbound.Tray`, Avalonia): background auto-sync, a numeric-loopback **one-time-code
  connect** flow that stores only a device-bound `device:upload` token, a deliberately minimal connected-device
  card, and explicit editable Desktop/Laptop/Steam Deck/Other setup before any hosted action. An **exact-body upload
  preview/confirmation** gates background sync until the current projection is consented; hardware specs and login
  auto-start remain local controls. Distribution uses a **Velopack installer + self-update** from GitHub Releases
  (Windows + Linux `AppImage` shipping; macOS unsigned/testing until notarized). Its release workflow now fails before
  packaging unless tag/props/changelog identity and the complete Release/Decky quality boundary pass. *Compiles and the
  logic is covered; a manual GUI/E2E pass is the owner's remaining step.*
- xUnit + Shouldly suites for the core and tray security flow, Decky pytest contract/parity coverage, and pinned
  `actionlint` checks for every GitHub Actions workflow.
  Verified on a real ~111-game /
  2-library install; MCP server smoke-tested over stdio (write→search round-trip, server instructions,
  get_profile_status).
- **Local only. Identity is the local machine owner; real auth slots in for the hosted layer.**

**Local snapshot scope:** installed + (with an API key) positive visible owned-game observations and
playtime, Steam accounts, device info, and the user's **local categories** with per-game tags. Web API
coverage is explicitly non-complete. Official hosted clients drop the account array and coarsen device
identity through projection v2 before upload.
Categories are read from the **modern Steam collections** (Chromium leveldb), falling back to the
legacy `sharedconfig.vdf` — the legacy file is stale for modern-UI users
([steam-collections.md](./steam-collections.md)).

## Roadmap (open core)

Local-first — prove the data model locally before anything depends on it.

1. **Distribution (mostly shipped):** the **tray agent** installs + self-updates via **Velopack** on a
   `tray-v*` tag — Windows (`Setup.exe`) and Linux (`AppImage`); macOS is an unsigned test artifact until
   notarized. The **CLI/MCP ship as .NET global tools** on NuGet (`dotnet tool install -g Shelfbound.Cli` /
   `Shelfbound.Mcp`) with independent versions; immutable library `v*` releases deliberately pack only
   Core/Query/Steam. Process: [releasing.md](./releasing.md). Remaining: macOS signing + notarization and pointing the
   tray at production URLs before a public build.
2. **Taste/profile depth:** user-data now merges into query results (filter by status/rating/completion);
   remaining — a "what Shelfbound remembers" review/edit view and optional metered LLM extraction.
3. **Remaining local data:** dynamic (`filterSpec`) collections (the modern-collections reader handles static ones
   today).
4. **Snapshot/export polish:** validation, import/export ergonomics for other clients.

Done: local scanner including Windows registry discovery, local categories (modern collections + legacy fallback), visible not-installed +
playtime observations (Steam Web API), the query engine (merging facts + user-data), the local MCP server (read +
write tools), the user-data store + identity seam, the tray installer + self-update with fail-closed release gates, and the
CLI/MCP packaged as .NET global tools.

> Hosted and paid features (if any) are developed separately and are intentionally out of scope here.

## Glossary

- **Snapshot** — versioned, portable export of local library context; the contract between scanner,
  local MCP, and any other consumer. See [snapshot-schema.md](./snapshot-schema.md).
- **Local mode** — everything runs on the user's machine; nothing leaves unless they export/upload.
- **Category definition** — the user's personal meaning for a Steam collection name.
- **Taste profile** — durable, user-owned signals about what the user likes/dislikes and why.
- **VDF / ACF** — Valve's text KeyValues format used by Steam local files.

## Documentation index

- [ARCHITECTURE.md](./ARCHITECTURE.md) — components, the snapshot seam, local/cloud boundary, data model.
- [DECISIONS.md](./DECISIONS.md) — technical/architecture decision log.
- [snapshot-schema.md](./snapshot-schema.md) — the snapshot contract in detail.
- [query-plan.md](./query-plan.md) — QueryPlan v1 shape, text grammar, diagnostics, capabilities, and corpus.
- [steam-collections.md](./steam-collections.md) — modern collections reader (stale-category fix): design + findings.
- [privacy-and-data.md](./privacy-and-data.md) — privacy principles and what is read.
- [mcp-design.md](./mcp-design.md) — local MCP tool design and memory-write guardrails.
- [oss-boundary.md](./oss-boundary.md) — what stays public vs private (open-core boundary) + leak check.
- [License summary](../../README.md#license) — the repo is AGPL-3.0-or-later.
