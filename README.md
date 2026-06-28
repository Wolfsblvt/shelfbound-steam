# Shelfbound

**AI-ready context for your real Steam library.**

Shelfbound is a personal gaming context layer: it reads your *real* Steam library — including
installed games per device and (soon) your local categories, notes, statuses, and taste — and makes
it available to AI tools through [MCP](https://modelcontextprotocol.io), locally or via a hosted
service. The point is to let ChatGPT/Claude reason about *your* library, not generic Steam API data.

> **Unofficial.** Shelfbound is not affiliated with or endorsed by Valve. "Steam" is used
> descriptively.

This repository (`shelfbound-steam`) is the **open-source local core**: domain models, the snapshot
contract, the local Steam scanner, and the CLI. The hosted service lives in a separate private repo.

## Status

Early but already useful, all local:

- **`shelfbound scan`** writes a versioned **snapshot** of your library — installed games across all
  libraries (names, install state, size, timestamps), your Steam accounts, the device, and your
  **local categories** (the collections you organize your library with). With a Steam Web API key it
  also adds **owned-but-not-installed games and playtime**. No install paths, credentials, or saves.
- **`shelfbound-mcp`** — a local **MCP server** that exposes the library to AI tools (ChatGPT/Claude):
  search by category / install state / playtime, library summary, game details, "what haven't I played?".
- **Per-game context** — the MCP server can also *remember* what you tell it: statuses
  (finished/paused/dropped), ratings, completion, category meanings, and freeform memories — stored
  locally and shared with the CLI.

Not built yet: modern dynamic collections, accounts, upload, and the hosted service. See
[docs/project/PROJECT.md](docs/project/PROJECT.md) for the roadmap.

## Quick start

Requires the **.NET 10 SDK**.

```bash
dotnet build
dotnet run --project src/Shelfbound.Cli -- setup        # one-time: shows config + how to add an API key
dotnet run --project src/Shelfbound.Cli -- scan --pretty
dotnet run --project src/Shelfbound.Cli -- profile      # what Shelfbound remembers about your library
```

**Steam Web API key (optional — for owned-but-not-installed games + playtime):** get one at
<https://steamcommunity.com/dev/apikey> (sign in, register any domain — `localhost` is fine), then
`shelfbound setup --steam-api-key <key>`. Also set your Steam profile **Game details → Public**, or the
API returns nothing. Both the CLI and the MCP server then use the saved key.

This writes `shelfbound-snapshot.json` (git-ignored — it lists your games, so treat it as personal).
Useful options: `--output <file>`, `--stdout`, `--steam-path <dir>`, `--device-name <name>`,
`--device-type …`, and `--steam-api-key <key>` (or `STEAM_WEB_API_KEY`) to add owned-but-not-installed
games + playtime via the Steam Web API. Run `shelfbound --help`.

### Local MCP server

`shelfbound-mcp` scans your library on startup and serves it to MCP-compatible AI clients over stdio.
Point your client (e.g. Claude Desktop) at the built `shelfbound-mcp` executable. It reads config from
the environment: `SHELFBOUND_STEAM_PATH` (else auto-detected), `STEAM_WEB_API_KEY` (optional, for
owned games + playtime), `SHELFBOUND_SNAPSHOT` (load a snapshot file instead of scanning). Read tools:
`search_library`, `get_library_summary`, `get_categories`, `get_game_details`, `find_installed_unplayed`.
Write/remember tools: `record_game_status`, `record_game_opinion`, `set_game_completion`,
`set_category_definition`, `remember`, plus `get_game_user_data` / `get_remembered`.

```bash
dotnet test
```

## Repository layout

The whole repository is licensed **AGPL-3.0-or-later** (see [License](#license)).

| Project | Purpose |
|---|---|
| `src/Shelfbound.Core` | Domain models + the versioned snapshot contract + serializer. |
| `src/Shelfbound.Steam` | Local Steam scanner + Steam Web API client + enrichment. |
| `src/Shelfbound.Query` | Deterministic query/filter/summary engine over a snapshot. |
| `src/Shelfbound.Storage` | Local config, identity seam, and the user-data store (statuses, ratings, memories). |
| `src/Shelfbound.Cli` | The `shelfbound` command-line tool (setup/scan/export). |
| `src/Shelfbound.Mcp` | The `shelfbound-mcp` local MCP server. |
| `tests/…` | Unit + integration tests. |

## Documentation

Start with [docs/project/PROJECT.md](docs/project/PROJECT.md); the snapshot contract is in
[docs/project/snapshot-schema.md](docs/project/snapshot-schema.md) and
[`schema/snapshot.v0.schema.json`](schema/snapshot.v0.schema.json).

## License

This project is licensed under **AGPL-3.0-or-later** (see [LICENSE](LICENSE)) to ensure that
improvements to hosted or modified versions remain available to users and the community. This keeps
the open-source core free for everyone and prevents it from being quietly turned into a closed
product. A separate hosted version may be offered for convenience; it is developed privately and is
out of scope for this repository.
