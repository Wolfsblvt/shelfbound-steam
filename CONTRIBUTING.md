# Contributing to Shelfbound

Thanks for your interest! Shelfbound is a maintained hobby/side project. Contributions are welcome,
within the scope below.

## Scope

This repository is the **free, open-source core**: the local Steam scanner, the snapshot contract,
the CLI, and (soon) a local MCP server. Hosted/account/paid features are a separate, private product
and are out of scope here. Good contributions: Steam-file parsing robustness, cross-platform support
(Linux/macOS/Steam Deck), the snapshot schema, the local MCP server, tests, and docs.

## Licensing of contributions

By contributing, you agree your contribution is licensed under the project's license,
**AGPL-3.0-or-later** (inbound = outbound). No CLA is required.

## Before you start

- For anything non-trivial, **open an issue first** to discuss the approach. Small, obvious fixes can
  go straight to a PR.
- Read [`docs/project/`](docs/project/) — especially `ARCHITECTURE.md`, `snapshot-schema.md`, and
  `privacy-and-data.md`. The snapshot is a versioned contract; schema changes need care.

## Development

Requires the **.NET 10 SDK**.

```bash
dotnet build
dotnet test
dotnet run --project src/Shelfbound.Cli -- scan --pretty
```

## Conventions

- Match the existing style (file-scoped namespaces, nullable enabled, small focused types).
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
