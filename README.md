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

Early. What works **today** is local and offline:

- `shelfbound scan` discovers your Steam install and writes a versioned **snapshot** of installed
  games across all libraries (names, install state, size, timestamps), your Steam accounts, the
  device, and your **local categories** (the collections you organize your library with) — with
  **no install paths, credentials, or save data**.

Not built yet: owned-but-not-installed games, modern dynamic collections, the local MCP server,
accounts, upload, and the hosted service. See [docs/project/PROJECT.md](docs/project/PROJECT.md) for
the roadmap.

## Quick start

Requires the **.NET 10 SDK**.

```bash
dotnet build
dotnet run --project src/Shelfbound.Cli -- scan --pretty
```

This writes `shelfbound-snapshot.json` (git-ignored — it lists your installed games, so treat it as
personal). Useful options: `--output <file>`, `--stdout`, `--steam-path <dir>`,
`--device-name <name>`, `--device-type desktop|laptop|steamDeck|server`. Run `shelfbound --help`.

```bash
dotnet test
```

## Repository layout

The whole repository is licensed **AGPL-3.0-or-later** (see [License](#license)).

| Project | Purpose |
|---|---|
| `src/Shelfbound.Core` | Domain models + the versioned snapshot contract + serializer. |
| `src/Shelfbound.Steam` | Local Steam scanner (VDF/ACF parsing, install discovery). |
| `src/Shelfbound.Cli` | The `shelfbound` command-line tool. |
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
