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

### Data scope — installed games + local categories
The local scan yields *installed* games per library plus the user's **local categories** (read from
the legacy `sharedconfig.vdf` `tags` store, which covers the common case). **Owned-but-not-installed**
still needs the Steam Web API; **modern dynamic collections** can live in the client's leveldb and are
not read yet. Both are deferred to focused follow-ups rather than shipped half-working.

### VDF parsing — hand-rolled minimal parser (no dependency)
The files we read use a simple quoted KeyValues subset; a small in-repo parser keeps v0
dependency-free and offline-buildable. *Alternative if edge cases bite:* `Gameloop.Vdf` (MIT).

### Test assertions — **Shouldly** (not FluentAssertions)
FluentAssertions 8.x became commercially licensed; Shouldly is free and a clean fit with xUnit.

### Steam discovery — candidate paths + override for v0
`SteamInstallLocator`: explicit `--steam-path` → `SHELFBOUND_STEAM_PATH` → per-OS default locations.
Windows registry (`SteamPath`) lookup for non-default installs is a planned enhancement.

### Query engine + local MCP server — the product seam
A reusable, deterministic `Shelfbound.Query` engine (filter/sort/summary) sits between the snapshot and
consumers; the **local MCP server** (`Shelfbound.Mcp`, official C# SDK, stdio) exposes read tools over
it. Keeping query logic out of the MCP layer lets the dashboard/hosted layer reuse it later.

### Steam Web API as composable enrichment
Owned-but-not-installed games + playtime come from the Steam Web API behind `ISteamWebApiClient`
(swappable/mockable). A **pure** `SteamWebEnricher` merges the fetched data into the local snapshot, so
the network call and the merge logic stay separable and testable. Requires a user-provided key
(`--steam-api-key` / `STEAM_WEB_API_KEY`); the key is never stored.

### User-data storage + identity/auth seam
Durable user/derived data (per-game status/rating/completion/aspects, scoped memories, category
meanings) lives in `Shelfbound.Storage`, **keyed by an owner/profile id** behind an identity seam
(`ProfileIdentity`). There is no local login — the machine owner is the identity (profile = the primary
Steam account, else `local`). The hosted layer swaps in auth-backed identity over the **same** store
contract (`IUserDataStore`) and data model. Local backend is a JSON-file-per-owner store with atomic
writes, shared consistently by the CLI and MCP server; SQLite/DB is the planned upgrade behind the same
interface. Kept separate from the raw snapshot (the derived-data category).

### Config + the Steam Web API key
Config (API key, active profile) is stored in the user's config dir via `shelfbound setup`, read by
both the CLI and the MCP server (key precedence: flag > env > config). The key is the user's own
read-only, rate-limited credential, stored plaintext in the protected config dir for now (OS-keystore
encryption — DPAPI / libsecret — is a planned hardening). Never in the repo, never logged.

### Surfacing user data — shared logic, per-surface presentation
"What Shelfbound remembers" appears in several places: the CLI (`shelfbound profile`), the local MCP
server (`get_remembered` / `get_profile_status`, rendered conversationally), and the future hosted
dashboard (web UI). Decision: **share the data model (`Shelfbound.Core.UserData`) and the derivation
logic (`Shelfbound.Query` — the merged view + `ProfileQuery`); reimplement only the presentation per
surface.** A cross-surface rendering layer (text vs web vs chat) isn't worth it — the shared layer is
the model + query/derivation, which every surface (including the hosted one) reuses. The dashboard
re-renders the same data over the hosted store rather than sharing rendering code.

### Onboarding via MCP — model-driven, profile-aware
`get_profile_status` reports whether the taste profile is set up and what to ask (suggested games to
rate, undefined category meanings). Its description plus **server-level instructions** push the model
to onboard the user (ask, then save via the write tools) when the profile is sparse, and to save
context whenever the user states an opinion/status/meaning. The deterministic profile logic lives in
the shared `ProfileQuery`; the model drives the conversation.

### Deferred (technical, recorded so it isn't re-litigated)
- **Distribution** — package the CLI/MCP server as a dotnet tool + GitHub Releases.
- **Modern dynamic collections** (leveldb) to complement the legacy categories already parsed.
- **Steam Deck** specifics (SD-card install location) and a future **Decky plugin** that emits the
  same snapshot contract.
