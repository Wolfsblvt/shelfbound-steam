# Contributing to Shelfbound

Thanks for your interest! Shelfbound is a maintained hobby/side project. Contributions are welcome,
within the scope below.

## Scope

This repository is the **free, open-source core**: the local Steam scanner, the snapshot contract,
the CLI, and (soon) a local MCP server. Hosted/account/paid features are a separate, private product
and are out of scope here. Good contributions: Steam-file parsing robustness, cross-platform support
(Linux/macOS/Steam Deck), the snapshot schema, the local MCP server, tests, and docs.

## Licensing of contributions

This project is open-core: it's free under **AGPL-3.0-or-later**, and the maintainer also runs a
separate paid hosted service built on the reusable libraries here (which keeps the open core free).
By contributing, you agree to the lightweight **[Contributor License Agreement](CLA.md)**: your work
is licensed under AGPL-3.0-or-later like any contribution, **and** you grant the maintainer the right
to also use/relicense it (including in the proprietary hosted service). You keep your copyright. See
[CLA.md](CLA.md) for the exact terms.

## Before you start

- For anything non-trivial, **open an issue first** to discuss the approach. Small, obvious fixes can
  go straight to a PR.
- Read [`docs/project/`](docs/project/) — especially `ARCHITECTURE.md`, `snapshot-schema.md`, and
  `privacy-and-data.md`. The snapshot is a versioned contract; schema changes need care.

## Development

Requires the **.NET 10 SDK**. The aggregate suite also runs the Decky backend tests; install their
Python dependencies once in an isolated environment:

```bash
python -m venv decky/.venv
# Windows:
decky/.venv/Scripts/python -m pip install -r decky/requirements-dev.txt
# Linux/macOS:
decky/.venv/bin/python -m pip install -r decky/requirements-dev.txt
```

```bash
dotnet build
pwsh scripts/test.ps1    # .NET + Decky pytest
pwsh scripts/lint.ps1    # C# report + Decky lint/format + pinned GitHub Actions actionlint
dotnet run --project src/Shelfbound.Cli -- scan --pretty
```

Decky frontend work additionally needs **Node.js 22**. Corepack reads the pinned pnpm version from
`decky/package.json`:

```bash
cd decky
corepack pnpm install --frozen-lockfile
corepack pnpm build
corepack pnpm lint
corepack pnpm format:check
```

## Conventions

- Match the existing style (file-scoped namespaces, nullable enabled, small focused types).
- C# style is reported by `dotnet format` but remains non-blocking; Decky ESLint and Prettier are CI gates.
- `Shelfbound.Core` stays pure (no file/network/environment access). Local-machine I/O lives in
  `Shelfbound.Steam` or `Shelfbound.Cli`.
- Add tests (xUnit + Shouldly) for behavior changes; add a regression test for bug fixes.
- **Privacy is a hard requirement:** don't read beyond the local files the scanner needs, and don't
  weaken snapshot privacy (no full install paths, credentials, or save data). See
  `docs/project/privacy-and-data.md`.
- When changing the snapshot shape, bump `SnapshotSchema.Version` and update
  `schema/snapshot.v0.schema.json` and `docs/project/snapshot-schema.md`.
- Keep PRs focused and reviewable; update affected docs in the same PR.

## Reporting bugs / requesting features

Use the issue templates. For security issues, see [SECURITY.md](SECURITY.md) — please don't open a
public issue.
