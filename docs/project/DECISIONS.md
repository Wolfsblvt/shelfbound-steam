# Shelfbound — Decision Log (open-source core)

Technical/architecture decisions for the public core. Curated, not an endless append log: when a
decision changes, the entry is **updated to the current truth** with a short note of what was
considered, rather than stacking superseded entries. Business/product decisions live privately.

---

## Foundation (2026-06-28)

### Name — **Shelfbound**, unofficial, for Steam
Brandable, library/backlog connotation, not Steam-locked long-term, "for Steam" helps discovery.
Always described as **unofficial, not affiliated with Valve**. Public core repo keeps the
`shelfbound-steam` name for SEO; rename possible if scope ever broadens far beyond Steam.

### Stack — **.NET `net10.0` (C#)** everywhere we control
Maintainer's primary language; fast, clean; strong server story for the (separate) hosted layer; and
the **official C# MCP SDK** (`ModelContextProtocol`, v1.0+, with Microsoft) makes MCP first-class.
Platform-forced exceptions (a future Decky plugin = Python/TS) interoperate via the snapshot JSON
contract, not shared code.

### Two repositories — public core, private product
`shelfbound-steam` (this repo, open source) holds the free local/core capability; the proprietary
hosted product lives in a separate private repo. *Why now:* public history is irreversible; the
commercial layer must never leak into public history.

### License — **single AGPL-3.0-or-later** for the whole public repo
One license for the whole published project (no per-file/per-project split). AGPL keeps the core
**free forever** and ensures modified/hosted versions stay open, which limits third parties from
quietly turning the free core into a closed commercial product. *Considered and rejected:* an
Apache-libs / AGPL-app split and MPL-2.0 — a public repo with mixed licenses is confusing, and AGPL
best matches a "free core + a separate private product" model. The maintainer retains rights to the
project; details of the separate product are recorded privately.

### Snapshot — the versioned, language-neutral **contract** (the seam)
One snapshot format connects scanner → local MCP → any other consumer, so parsing and the data model
are never duplicated. JSON Schema + `Shelfbound.Core` types; `schemaVersion` (semver) in every
document. See [snapshot-schema.md](./snapshot-schema.md).

### Local-first build order
Build the local core (scanner → snapshot → local MCP) before anything networked. It is the
differentiator, has zero auth/infra/cost, can be dogfooded immediately on a real library, and
pressure-tests the snapshot schema before anything depends on it.

### Privacy-first snapshot
The snapshot **omits full filesystem paths** (libraries → index + label; games → relative install-dir
name only). `device.id` is a **random GUID persisted locally**, not derived from hardware/account. No
credentials, saves, screenshots, or arbitrary files are ever read. So a snapshot is safe-by-default
even if later exported/uploaded. See [privacy-and-data.md](./privacy-and-data.md).

### v0 data scope — installed games only
The local scan reliably yields *installed* games per library. **Owned-but-not-installed** needs the
Steam Web API; **local collections/categories** use version-dependent formats. Both are deferred to
focused follow-ups rather than shipped half-working. `SnapshotGame.categories` is reserved but
currently always empty.

### VDF parsing — hand-rolled minimal parser (no dependency)
The files we read use a simple quoted KeyValues subset; a small in-repo parser keeps v0
dependency-free and offline-buildable. *Alternative if edge cases bite:* `Gameloop.Vdf` (MIT).

### Test assertions — **Shouldly** (not FluentAssertions)
FluentAssertions 8.x became commercially licensed; Shouldly is free and a clean fit with xUnit.

### Steam discovery — candidate paths + override for v0
`SteamInstallLocator`: explicit `--steam-path` → `SHELFBOUND_STEAM_PATH` → per-OS default locations.
Windows registry (`SteamPath`) lookup for non-default installs is a planned enhancement.

### Deferred (technical, recorded so it isn't re-litigated)
- Local **collections/categories** parsing and **owned-not-installed** (Steam Web API).
- **Local MCP server** (next major piece) — design in [mcp-design.md](./mcp-design.md).
- **Steam Deck** specifics (SD-card install location) and a future **Decky plugin** that emits the
  same snapshot contract.
