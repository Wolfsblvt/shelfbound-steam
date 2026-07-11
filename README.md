# Shelfbound

[![CI](https://github.com/Wolfsblvt/shelfbound-steam/actions/workflows/ci.yml/badge.svg)](https://github.com/Wolfsblvt/shelfbound-steam/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/Wolfsblvt/shelfbound-steam/graph/badge.svg)](https://codecov.io/gh/Wolfsblvt/shelfbound-steam)
[![NuGet](https://img.shields.io/nuget/v/Shelfbound.Core?logo=nuget&label=NuGet&color=004880)](https://www.nuget.org/packages/Shelfbound.Core)
[![license](https://img.shields.io/github/license/Wolfsblvt/shelfbound-steam?color=blue)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

**AI-ready context for your real Steam library.**

Shelfbound is a personal gaming context layer: it reads your *real* Steam library — installed games
per device, your local categories (collections), notes, statuses, and taste — and makes it available
to AI tools through [MCP](https://modelcontextprotocol.io), locally or via a hosted service. The point
is to let ChatGPT/Claude reason about *your* library, not generic Steam API data.

> **Unofficial.** Shelfbound is not affiliated with or endorsed by Valve. "Steam" is used
> descriptively.

This repository (`shelfbound-steam`) is the **open-source local core**: domain models, the snapshot
contract, the local Steam scanner, and the CLI. The hosted service lives in a separate private repo.

## Status

Early but already useful, all local:

- **`shelfbound scan`** writes a versioned **snapshot** of your library — installed games across all
  libraries (names, install state, size, timestamps), your Steam accounts, the device, and your
  **local categories** (read from your **modern Steam collections**, falling back to the legacy store).
  With a Steam Web API key it also adds **owned-but-not-installed games and playtime**. No install
  paths, credentials, or saves.
- **`shelfbound-mcp`** — a local **MCP server** that exposes the library to AI tools (ChatGPT/Claude):
  search by category / install state / playtime, library summary, game details, "what haven't I played?".
- **Per-game context** — the MCP server can also *remember* what you tell it: statuses
  (finished/paused/dropped), ratings, completion, category meanings, and freeform memories — stored
  locally and shared with the CLI.

Also built: a cross-platform **tray app** (privacy preview, consent-gated background sync + account
connect) and **`shelfbound upload`** (send a minimized hosted projection to a Shelfbound server).
Still to come locally: dynamic (rule-based) collections, Windows-registry install discovery, and
real-hardware validation/distribution of the Decky prototype. The hosted service lives in a separate
private repo. See [docs/project/PROJECT.md](docs/project/PROJECT.md) for the roadmap.

## Quick start

**Install as global tools** (published to NuGet on each release; requires the **.NET 10 runtime**):

```bash
dotnet tool install -g Shelfbound.Cli    # the `shelfbound` command
dotnet tool install -g Shelfbound.Mcp    # the `shelfbound-mcp` server
shelfbound setup                         # one-time: shows config + how to add an API key
shelfbound scan --pretty
```

**Or run from source** (requires the **.NET 10 SDK**):

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

### Upload to a Shelfbound server (optional)

`shelfbound upload` scans locally, derives a whitelist-only hosted projection, and sends that minimized
body so the hosted MCP/dashboard can read your library without your machine online. Preview the exact
compact body first (no server or token required), then upload:

```bash
shelfbound upload --dry-run                            # prints exact body; sends nothing
shelfbound upload --server <url> --token <token>     # or SHELFBOUND_SERVER / SHELFBOUND_TOKEN
```

A hosted body includes the user-chosen/neutral device label, random device id, coarse OS/specs,
libraries, games, collections, and stats. It drops the complete Steam-account array (login, persona,
and Steam ids), never auto-uploads the machine hostname, and omits exact OS builds. Game and collection
names remain personal; a game name can reveal a private/non-Steam title from another producer.
Official clients also preserve successful server warnings and distinguish throttling, token scope,
device-cap, invalid-snapshot, and payload-size failures instead of collapsing them to a status number.

A one-shot upload is free. Continuous `--watch` sync is a paid (Pro/Lifetime) feature, enforced by the
server. See [the privacy contract](docs/project/privacy-and-data.md) for the exact field boundary and
preview behavior.

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
| `src/Shelfbound.Client` | Shared scan-to-snapshot builder + Shelfbound server client, reused by the CLI and tray. |
| `src/Shelfbound.Cli` | The `shelfbound` command-line tool (setup/scan/profile/upload). |
| `src/Shelfbound.Tray` | The cross-platform tray app (Avalonia): background sync, status, and account connect. |
| `src/Shelfbound.Mcp` | The `shelfbound-mcp` local MCP server. |
| `decky/` | Steam Deck (Decky) plugin **prototype** — own Python/TS toolchain, emits the same snapshot contract. Hardware-gated; see [decky/README.md](decky/README.md). |
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
