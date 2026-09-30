# <img src="assets/icon.svg" alt="" width="32" height="32"> Shelfbound

[![CI](https://img.shields.io/github/actions/workflow/status/Wolfsblvt/shelfbound-steam/ci.yml?branch=main&logo=githubactions&logoColor=white&label=CI)](https://github.com/Wolfsblvt/shelfbound-steam/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Shelfbound.Core?logo=nuget&label=Shelfbound.Core&color=004880)](https://www.nuget.org/packages/Shelfbound.Core)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![AGPL-3.0-or-later](https://img.shields.io/github/license/Wolfsblvt/shelfbound-steam?color=blue)](LICENSE)

**AI-ready context for your real Steam library.**

Shelfbound turns the Steam library on this machine—installed games, local collections, device and
storage context, optional playtime observations, and the preferences you choose to save—into structured
local data and MCP tools. It lets an AI client reason about *your* backlog instead of a generic catalogue.

The local scanner and MCP server need no Shelfbound account, token, or hosted service. A scan reads
local Steam files; if a Steam Web API key is supplied through the environment or saved by `setup`,
it also requests visible game observations from Steam. Upload to a configured server is a separate action.

> [!NOTE]
> Shelfbound is unofficial and is not affiliated with or endorsed by Valve. “Steam” is used
> descriptively.

## First success: inspect the library on this machine

The current source requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and a
Steam desktop installation.

```bash
git clone https://github.com/Wolfsblvt/shelfbound-steam.git
cd shelfbound-steam
dotnet run --project src/Shelfbound.Cli -- scan --pretty
```

Shelfbound prints a summary and writes `shelfbound-snapshot.json`. The file is a portable, versioned
record of what this device actually exposed: installed games per Steam library, collections, size and
recency facts, device/storage context, and the library's evidence scope.

Treat the snapshot as personal data. It can contain identifiers, login names, and persona names for
every account in Steam's local `loginusers.vdf`, plus the names of your games and collections. It never
contains credentials, save files, screenshots, full install paths, hardware serials, or arbitrary files.

Useful scan options:

```text
--output <file>       choose the snapshot path
--stdout              write JSON to standard output
--steam-path <dir>    override Steam discovery
--device-name <name>  choose a non-secret device label
--device-type <type>  desktop | laptop | steamDeck | server | unknown
```

Run `dotnet run --project src/Shelfbound.Cli -- --help` for the complete CLI surface.

## Talk to the library through MCP

Build the current local MCP server:

```bash
dotnet build src/Shelfbound.Mcp/Shelfbound.Mcp.csproj -c Release
```

For MCP clients that use the common `mcpServers` JSON shape, point the client at the built assembly and
the snapshot from the first step:

```json
{
  "mcpServers": {
    "shelfbound": {
      "command": "dotnet",
      "args": [
        "<absolute-repo-path>/src/Shelfbound.Mcp/bin/Release/net10.0/shelfbound-mcp.dll"
      ],
      "env": {
        "SHELFBOUND_SNAPSHOT": "<absolute-repo-path>/shelfbound-snapshot.json"
      }
    }
  }
}
```

Replace both placeholders with absolute paths. Without `SHELFBOUND_SNAPSHOT`, the server scans Steam
when it starts. `SHELFBOUND_STEAM_PATH` overrides discovery, and `STEAM_WEB_API_KEY` enables the
optional enrichment described below.

A useful first prompt is:

> Summarize the scope and collections in my library, then show five installed games I have probably
> not started.

The server is local, stdio-only MCP. Protocol messages use stdout; redacted diagnostics use stderr.
Its current tools are:

| Purpose | Tools |
|---|---|
| Read and search | `get_library_summary`, `get_categories`, `search_library`, `get_game_details`, `find_installed_unplayed`, `get_recommendations` |
| Inspect saved context | `get_profile_status`, `get_game_user_data`, `get_remembered` |
| Save or correct explicit context | `record_game_status`, `record_game_opinion`, `set_game_completion`, `set_category_definition`, `remember`, `delete_memory` |

Search can combine title text, install state, uncategorized state, collection inclusion/exclusion,
playtime, status, rating, completion, played-elsewhere state, sorting, and a result limit.
Recommendations are deterministic cards over observed facts and saved context; the AI client presents
and discusses them.

Saved context belongs to the local profile store and can include statuses, ratings, completion,
liked/disliked aspects, collection meanings, and scoped memories. MCP instructions tell the client to
save only facts the user explicitly states—never an inferred preference—and `delete_memory` provides a
correction path. Inspect the same state without an MCP client:

```bash
dotnet run --project src/Shelfbound.Cli -- profile
```

## What Shelfbound knows—and what it does not

### Local scan

Shelfbound reads:

- Steam library metadata and app manifests for installed-game presence, names, relative install
  folders, sizes, and timestamps;
- every account in the local `loginusers.vdf` file (the most recent account is selected for Web API enrichment);
- current static Steam collections from the client's Chromium Local Storage, with the legacy
  `sharedconfig.vdf` store as a fallback;
- best-effort device specifications and per-library storage kind/capacity.

Collection reading is best-effort outside its validated Windows path and can lag Steam's last unflushed
edit. Dynamic rule-based collections (`filterSpec`) and non-Steam shortcuts are not part of the current
scan.

An installed manifest is evidence that an install is recorded on this device. It is **not** proof that
the active account owns the game or can still launch it.

### Optional Steam Web API enrichment

A user-provided Steam Web API key can add positive observations for visible not-installed games,
playtime, and last-played time:

```bash
# Set STEAM_WEB_API_KEY in this process first, then save it without putting it in argv.
dotnet run --project src/Shelfbound.Cli -- setup --steam-api-key-env
dotnet run --project src/Shelfbound.Cli -- scan --pretty
```

The profile's **Game details** visibility must be public enough for `GetOwnedGames`. The key is accepted
from the environment or standard input, never as a command-line value, and is not included in surfaced
request errors. A saved key is currently plaintext in the user's config directory (mode `0600` on Unix;
on Windows it inherits the user-profile directory ACL); OS-keystore encryption is not implemented yet.

To scan offline after saving a key, set `STEAM_WEB_API_KEY` to one space for the scan process. This
overrides the saved key and disables enrichment; leaving the variable unset uses the saved key. For
example, in a POSIX shell:

```bash
STEAM_WEB_API_KEY=' ' dotnet run --project src/Shelfbound.Cli -- scan --pretty
```

This enrichment is useful but not complete. Shelfbound labels its evidence honestly:

| Scope | Meaning |
|---|---|
| `installedOnly` | Games observed from installed manifests on this device. |
| `observedSubset` | Installed facts plus positive visibility-gated observations; absence proves nothing. |
| `fullLibrary` | Reserved for a source that explicitly guarantees completeness. No current Steam scan path produces it. |

Under either partial scope, an empty search result does **not** mean “not owned” or “not available.”

## Privacy and network behavior

| Action | Network behavior | Data boundary |
|---|---|---|
| `scan` | Calls Steam's `GetOwnedGames` when an environment or saved API key is present and a local account is found; otherwise no network request. | Writes the complete local snapshot you chose. |
| MCP startup | When it scans without an existing snapshot, the same key rule applies; loading an existing snapshot makes no Steam request. | Library facts and saved context remain local. |
| `setup` | No request is needed to save a key supplied through stdin/environment. | The key is kept in local config and never added to a snapshot. |
| `upload --dry-run` | The snapshot build may call Steam's `GetOwnedGames` under the same key rule; it sends nothing to a Shelfbound server. | Prints the exact compact hosted body. |
| `upload` | The snapshot build may call Steam under the same key rule; it posts to a Shelfbound server only after you provide a server and bearer token. | Sends the prepared whitelist projection to that configured server. |

The complete local snapshot is deliberately richer than the hosted projection and is **not**
upload-safe by itself. The official projection:

- drops the complete `steamAccounts` array;
- replaces automatic hostname input with a neutral label;
- coarsens the exact OS description;
- retains the random device id, chosen device label, coarse specs, libraries, storage capacity, games,
  collections, and aggregate facts needed by the receiving product;
- still contains personal game and collection names.

The CLI does not expose the interactive clients' optional Private-game exclusion setting; its preview
contains every game row in the prepared projection. Preview those exact bytes without a token or server:

```bash
dotnet run --project src/Shelfbound.Cli -- upload --dry-run
```

A real upload additionally requires `SHELFBOUND_SERVER` or `--server`, plus `SHELFBOUND_TOKEN` in the
environment. Availability, account behavior, limits, and production endpoints belong to the receiving
server; this repository does not promise them.

See [Privacy and data](docs/project/privacy-and-data.md) for the field-level contract and
[Security](SECURITY.md) for private vulnerability reporting.

## Public core and hosted companion

This repository is the free, AGPL-licensed local core:

```text
local Steam files ──> scanner ──> snapshot v0.6.0 ──┬─> query engine + local profile ──> MCP (stdio)
optional Steam API ───── positive observations ──────┘
                                                     └─> whitelist projection ──> optional configured server
```

The separate Shelfbound Cloud repository is a proprietary, pre-alpha companion. It consumes the
published snapshot contract and open-core packages; it does not parse Steam files server-side. The
local scanner, snapshot, query engine, profile store, and MCP server work without it.

That separation is intentional, not a trial funnel hidden in the wiring. This README does not present
hosted accounts, plans, quotas, dashboards, or future endpoints as part of the open-source checkout.

## Status and distribution

The supported first-success path in this README is the CLI and local MCP server from current source.

The reusable `Shelfbound.Core`, `Shelfbound.Query`, and `Shelfbound.Steam` libraries are released as
immutable NuGet packages. `Shelfbound.Cli` and `Shelfbound.Mcp` also have independently versioned .NET
global-tool packages:

```bash
dotnet tool install -g Shelfbound.Cli
dotnet tool install -g Shelfbound.Mcp
```

Those tool packages can lag `main`; check their package pages and release notes before assuming parity
with the source capabilities documented here.

Other repository surfaces have narrower status:

- `src/Shelfbound.Tray` contains the cross-platform uploader/tray source, including consent preview and
  upload-only device connection. It is not the quick-start path above.
- `decky/` is an exploratory Decky Loader prototype. It is tested off-device but has never run on a real
  Steam Deck and is not a public Deck-support claim or store release.

The [GitHub Releases page](https://github.com/Wolfsblvt/shelfbound-steam/releases) records the
published library stream. Check the separate [CLI](https://www.nuget.org/packages/Shelfbound.Cli)
and [MCP](https://www.nuget.org/packages/Shelfbound.Mcp) package pages for their tool versions.

## Architecture and repository map

The versioned JSON snapshot is the seam. Steam parsing happens once in `Shelfbound.Steam`; local query,
MCP, tools, and optional upload consume the resulting contract instead of re-parsing client files.

| Path | Responsibility |
|---|---|
| `src/Shelfbound.Core` | Snapshot and user-context models, schema identity, serialization. |
| `src/Shelfbound.Steam` | Local Steam discovery/parsing plus optional Web API enrichment. |
| `src/Shelfbound.Query` | Deterministic search, summaries, recommendations, profile derivation, and the QueryPlan grammar contract. |
| `src/Shelfbound.Storage` | Local config, profile identity, and atomic user-data persistence. |
| `src/Shelfbound.Mcp` | Local stdio MCP adapter over the snapshot/query/profile seams. |
| `src/Shelfbound.Cli` | `setup`, `profile`, `scan`, and explicit `upload` commands. |
| `src/Shelfbound.Client` | Shared snapshot builder, hosted whitelist projection, and server client. |
| `src/Shelfbound.Tray` | Avalonia tray/uploader client source. |
| `schema/` and `contracts/` | Machine-readable snapshot and cross-consumer conformance contracts. |
| `decky/` | Hardware-gated Python/TypeScript prototype using the same snapshot/projection contract. |
| `tests/` | .NET tests and contract fixtures. |

Start deeper in [Project documentation](docs/project/), especially
[Architecture](docs/project/ARCHITECTURE.md),
[Snapshot schema](docs/project/snapshot-schema.md),
[MCP design](docs/project/mcp-design.md), and
[Project status](docs/project/PROJECT.md).

## Development and contribution

The normal local checks are:

```bash
dotnet build
dotnet test
pwsh scripts/test.ps1
pwsh scripts/lint.ps1
```

The aggregate scripts also exercise Decky's Python/TypeScript contract surface; see
[CONTRIBUTING.md](CONTRIBUTING.md) for the isolated Python environment, Node 22 requirements, coding
conventions, schema-change rules, and the lightweight contributor licence agreement.

Open an issue before substantial changes. Use
[the issue templates](https://github.com/Wolfsblvt/shelfbound-steam/issues/new/choose) for bugs or
feature requests. Questions are handled on a best-effort basis; GitHub Discussions is not currently
enabled for this repository. Follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## License, contributions, and marks

The entire public repository—including application artwork—is licensed
[AGPL-3.0-or-later](LICENSE). Modified network services must make the corresponding source available
under the licence's terms.

Contributions remain AGPL in the public project and are also covered by the
[Contributor License Agreement](cla.md), which grants the maintainer additional reuse/relicensing
rights for the separate proprietary companion. Contributors retain their copyright.

The Shelfbound name and marks are covered by the separate
[trademark notice](trademarks.md). The copyright licence does not grant permission to imply endorsement
of a modified or unrelated product.
