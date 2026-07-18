# Shelfbound

[![quality gates](https://img.shields.io/github/actions/workflow/status/Wolfsblvt/shelfbound-steam/ci.yml?branch=main&logo=githubactions&logoColor=white&label=quality%20gates)](https://github.com/Wolfsblvt/shelfbound-steam/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/Wolfsblvt/shelfbound-steam/graph/badge.svg)](https://codecov.io/gh/Wolfsblvt/shelfbound-steam)
[![C# style](https://img.shields.io/badge/C%23%20style-report--only-6c757d?logo=dotnet&logoColor=white)](.editorconfig)
[![Decky quality](https://img.shields.io/badge/Decky-ESLint%20%2B%20Prettier-5a45ff?logo=eslint&logoColor=white)](decky/package.json)
[![Decky tests](https://img.shields.io/badge/Decky%20tests-pytest-0a9edc?logo=pytest&logoColor=white)](decky/tests)
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
  With a Steam Web API key it also adds **visible not-installed game observations and playtime**.
  Steam does not guarantee that visibility-gated result is complete; Shelfbound keeps useful positive
  rows and never treats absence as non-ownership. No install paths, credentials, or saves.
- **`shelfbound-mcp`** — a local **MCP server** that exposes the library to AI tools (ChatGPT/Claude):
  search by category / install state / playtime, library summary, game details, "what haven't I played?".
- **Per-game context** — the MCP server can also *remember* what you tell it: statuses
  (finished/paused/dropped), ratings, completion, category meanings, and freeform memories — stored
  locally and shared with the CLI.

Also built: a cross-platform **tray app** (privacy preview, consent-gated background sync + account
connect) and **`shelfbound upload`** (send a minimized hosted projection to a Shelfbound server).
Steam discovery checks an explicit path, `SHELFBOUND_STEAM_PATH`, the current user's Windows Steam
registry path when applicable, then platform defaults. Still to come locally: dynamic (rule-based)
collections and real-hardware validation/distribution of the Decky prototype. The hosted service lives
in a separate private repo. See [docs/project/PROJECT.md](docs/project/PROJECT.md) for the roadmap.

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

**Steam Web API key (optional — for additional visible games + playtime):** get one at
<https://steamcommunity.com/dev/apikey> (sign in, register any domain — `localhost` is fine), then
run `shelfbound setup --steam-api-key-stdin` and provide the key as one line on standard input, or set
`STEAM_WEB_API_KEY` and run `shelfbound setup --steam-api-key-env`. Also set your Steam profile
**Game details → Public**, or the API may return no usable game list. Even with that visibility, the
result is an observed subset rather than proof of a complete library. Both the CLI and MCP server use
the saved key, warn on missing/empty/malformed responses, and never include the key in warnings. Secrets
are deliberately not accepted as command-line arguments.

This writes `shelfbound-snapshot.json` (git-ignored — it lists your games, so treat it as personal).
Useful options: `--output <file>`, `--stdout`, `--steam-path <dir>`, `--device-name <name>`,
and `--device-type …`. Set `STEAM_WEB_API_KEY` or use the saved configuration to add visible game and
playtime observations via the Steam Web API. Run `shelfbound --help`.

### Upload to a Shelfbound server (optional)

`shelfbound upload` scans locally, derives a whitelist-only hosted projection, and sends that minimized
body so the hosted MCP/dashboard can read your library without your machine online. Preview the exact
compact body first (no server or token required), then upload:

```bash
shelfbound upload --dry-run                            # prints exact body; sends nothing
shelfbound upload --server <url>                       # token comes from SHELFBOUND_TOKEN
```

Set `SHELFBOUND_TOKEN` in the process environment before uploading; bearer tokens are not accepted
in argv, where shell history and process inspection could expose them. `SHELFBOUND_SERVER` can replace
the non-secret `--server` option.

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
additional visible games + playtime), `SHELFBOUND_SNAPSHOT` (load a snapshot file instead of scanning). Read tools:
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
| `src/Shelfbound.Query` | Deterministic query/filter/summary engine plus the versioned QueryPlan grammar contract. |
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
[`schema/snapshot.v0.schema.json`](schema/snapshot.v0.schema.json). The cross-surface query contract is
documented in [docs/project/query-plan.md](docs/project/query-plan.md).

## License

This project is licensed under **AGPL-3.0-or-later** (see [LICENSE](LICENSE)) to ensure that
improvements to hosted or modified versions remain available to users and the community. This keeps
the open-source core free for everyone and prevents it from being quietly turned into a closed
product. A separate hosted version may be offered for convenience; it is developed privately and is
out of scope for this repository.

The whole repository, including copyright in the tray artwork, remains under that license. Shelfbound names and marks
are also subject to the separate, narrow [trademark notice](TRADEMARKS.md); the copyright license does not grant
trademark rights beyond applicable law.
