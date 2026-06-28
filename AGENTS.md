# AGENTS.md — shelfbound-steam

Open-source local core of **Shelfbound**: reads a real Steam library and produces a versioned
snapshot for AI tools (MCP). .NET 10 / C#. Unofficial; not affiliated with Valve.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/Shelfbound.Cli -- setup           # store Steam Web API key in local config
dotnet run --project src/Shelfbound.Cli -- scan --pretty   # writes shelfbound-snapshot.json
dotnet run --project src/Shelfbound.Cli -- profile         # local "what Shelfbound remembers" view
dotnet run --project src/Shelfbound.Mcp                     # local MCP server (stdio)
```

## Project-specific constraints

- **This repo is public and open source.** Never put hosted/product, accounts/auth, billing, secrets,
  or monetization/strategy docs here — those belong in the separate private product repo. Public
  history is forever.
- **License: whole repo is AGPL-3.0-or-later** (single license; see `/LICENSE` and the README). Keep
  `<PackageLicenseExpression>AGPL-3.0-or-later</PackageLicenseExpression>` on new projects.
- **`Shelfbound.Core` stays pure** — no file/network/environment access. Local-machine I/O lives in
  `Shelfbound.Steam` (Steam files) or `Shelfbound.Cli` (device/args/output).
- **The snapshot is the contract.** Don't duplicate Steam parsing elsewhere; extend the snapshot
  (and bump `SnapshotSchema.Version` + `schema/snapshot.v0.schema.json`) when adding fields.
- **Privacy is a hard requirement.** No full install paths, credentials, or saves in snapshots. See
  `docs/project/privacy-and-data.md` before touching the scanner or snapshot shape.
- Tests use **Shouldly** (xUnit + Shouldly).

## Docs (read before non-trivial work)

`docs/project/` — `PROJECT.md` (overview/roadmap), `ARCHITECTURE.md`, `DECISIONS.md`,
`snapshot-schema.md`, `privacy-and-data.md`, `mcp-design.md`. Keep them in sync when
scope/architecture/decisions change. Product/business/monetization docs are maintained privately.
