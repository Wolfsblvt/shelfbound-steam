# Shelfbound — Project Overview (open-source core)

> Status as of 2026-06-28. This file is the living overview of the **open-source core**. Read it first.

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

A plain Steam Web API wrapper is not interesting — anyone can expose owned games and playtime. The
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
- Snapshot contract `v0.4.0` (`schema/snapshot.v0.schema.json`, models in `Shelfbound.Core`).
- Local Steam scanner (`Shelfbound.Steam`): install discovery, `libraryfolders.vdf`,
  `appmanifest_*.acf`, `loginusers.vdf`, **local categories** (`sharedconfig.vdf`), a minimal VDF parser.
- **Steam Web API** client + enrichment: owned-but-not-installed games and playtime (with an API key).
  The snapshot carries a **library scope** (`installedOnly` vs `fullLibrary`) so consumers never read
  "not found" as "not owned" when no key was used; the CLI says so loudly.
- **`Shelfbound.Query`**: deterministic filter/sort/summary over a **merged library view** (snapshot
  facts + user-data) — search by category, install state, playtime, **status, rating, and completion**;
  **recency** as human phrases (installed/last-played + "added N ago", inferred from first-seen).
- **Local MCP server** (`Shelfbound.Mcp`, stdio): read tools (`search_library`, `get_library_summary`,
  `get_categories`, `get_game_details`, `find_installed_unplayed`), write/remember tools
  (`record_game_status`, `record_game_opinion`, `set_game_completion`, `set_category_definition`,
  `remember`, `delete_memory`, `get_game_user_data`, `get_remembered`), an onboarding endpoint
  (`get_profile_status`), and **server instructions** that push the model to save context and onboard.
- **`Shelfbound.Storage`**: local config (API key), the **identity seam** (owner/profile), and a
  user-data store — per-game status/rating/completion/aspects, scoped memories, category meanings, and
  **first-seen** tracking (a "recently added/bought" proxy, since Steam exposes no purchase date) —
  shared by the CLI and MCP server.
- CLI (`Shelfbound.Cli`): `shelfbound setup` (API key), `shelfbound scan` (+ enrichment), and
  `shelfbound profile` (a local "what Shelfbound remembers" view).
- xUnit + Shouldly tests (37). Verified on a real ~111-game / 2-library install; MCP server
  smoke-tested over stdio (write→search round-trip, server instructions, get_profile_status).
- **Local only. Identity is the local machine owner; real auth slots in for the hosted layer.**

**Data scope:** installed + (with an API key) owned-but-not-installed Steam games, playtime, Steam
accounts, device info, and the user's **local categories** with per-game tags. Categories come from the
**legacy** `sharedconfig.vdf`, which is **stale for modern-UI collection users** — the fix (read the
modern Chromium-leveldb collections) is designed + validated but not yet built
([steam-collections.md](./steam-collections.md)).

## Roadmap (open core)

Local-first — prove the data model locally before anything depends on it.

1. **Distribution:** package the CLI/MCP server as a `dotnet tool` + GitHub Releases so others can install.
2. **Taste/profile depth:** user-data now merges into query results (filter by status/rating/completion);
   remaining — a "what Shelfbound remembers" review/edit view and optional metered LLM extraction.
3. **Remaining local data:** modern collections reader (Chromium leveldb — designed + validated, fixes
   stale categories; see [steam-collections.md](./steam-collections.md)), Steam Deck SD-card awareness,
   Windows registry-based install discovery.
4. **Snapshot/export polish:** validation, import/export ergonomics for other clients.

Done: local scanner, local categories, owned-not-installed + playtime (Steam Web API), the query
engine (merging facts + user-data), the local MCP server (read + write tools), and the user-data store + identity seam.

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
- [steam-collections.md](./steam-collections.md) — modern collections reader (stale-category fix): design + findings.
- [privacy-and-data.md](./privacy-and-data.md) — privacy principles and what is read.
- [mcp-design.md](./mcp-design.md) — local MCP tool design and memory-write guardrails.
- [License summary](../../README.md#license) — the repo is AGPL-3.0-or-later.
