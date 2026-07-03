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
The local scan yields *installed* games per library plus the user's **local categories**.
**Owned-but-not-installed** needs the Steam Web API, so the snapshot carries an explicit **library
scope** (`installedOnly` vs `fullLibrary`, on `stats.scope`): without a key the scan is installed-only,
and the CLI/MCP say so loudly so a missing game is never read as "not owned" (schema bumped to `v0.4.0`).
Categories are read from the **modern Steam collections** (Chromium-leveldb), falling back to the legacy
`sharedconfig.vdf` `tags` store — the legacy file is **stale for users who manage collections in the
modern Steam UI** (confirmed: it reported wrong categories that an AI then repeated). See the
"Modern collections" decision below and [steam-collections.md](./steam-collections.md).

### VDF parsing — hand-rolled minimal parser (no dependency)
The files we read use a simple quoted KeyValues subset; a small in-repo parser keeps v0
dependency-free and offline-buildable. *Alternative if edge cases bite:* `Gameloop.Vdf` (MIT).

### Modern collections — hand-rolled Chromium-leveldb reader (no dependency), legacy fallback
The legacy `sharedconfig.vdf` categories are **stale for modern-UI users** (confirmed: it reported wrong
categories an AI then repeated). The current collections live in the Steam client's Chromium **Local
Storage leveldb** (`cloud-storage-namespace-1`). Decision (**implemented** in `Shelfbound.Steam.Collections`):
**read them with a small hand-rolled reader** (snappy decompression + LevelDB SSTable + WAL + Chromium
LocalStorage decode + collections JSON), the scanner preferring it and **falling back to the legacy
`sharedconfig.vdf`** so a failed/empty modern read is never worse than today. *Considered and rejected:*
a native LevelDB/RocksDB dependency (too heavy/per-RID for a cross-platform open-core lib, and it still
needs a copy-and-open dance to dodge Steam's lock) and doing nothing (categories are a headline feature
and were wrong). Validated end-to-end against a real install (Slay the Princess → `Finished`). Known
cost: it parses Steam's internal, undocumented, in-flux cache, so it's **best-effort and maintained** —
Windows-solid, can lag the live client by the last unflushed edit, dynamic `filterSpec` collections
deferred. Full findings + algorithm: [steam-collections.md](./steam-collections.md).

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
- **Distribution** — resolved; see "Packaging & distribution" below (tray via Velopack, CLI/MCP as .NET tools).
- **Modern dynamic collections** (leveldb) to complement the legacy categories already parsed.
- **Steam Deck** specifics (SD-card install location) and a future **Decky plugin** that emits the
  same snapshot contract.

---

## Packaging & distribution (2026-07-02)

### Tray installer + auto-update — **Velopack**, publishing to GitHub Releases
The tray agent ships as a real installer that **self-updates**, using **Velopack** (`vpk`) — one .NET-native
toolchain that produces both the installer *and* delta/full auto-update packages, cross-platform. The app
calls `VelopackApp.Build().Run()` first in `Main` and checks a `GithubSource` for updates; the update path is
guarded by `UpdateManager.IsInstalled`, so source / `dotnet run` builds never self-update. The distribution
channel is **GitHub Releases** (free, fits open-core, no extra hosting), built by the `Release Tray` workflow
on a **`tray-v*`** tag — a stream deliberately separate from any future CLI/`dotnet tool` release so their
assets never collide. *Considered and rejected:* Squirrel.Windows and Clowd.Squirrel (Windows-only /
superseded by Velopack), MSIX (Windows-only, awkward sideload signing + update story), and Inno/WiX + a
hand-rolled updater (two tools, and we'd own the update logic — the kickoff explicitly said "pick one
toolchain that does both"). The `dotnet tool` route stays for the headless CLI/MCP, not a GUI a non-dev
installs.

### Platform rollout — Windows + Linux shipping, macOS deferred
Windows (`Setup.exe`) and Linux (`AppImage`, self-contained `linux-x64` — the Steam Deck desktop arch) both
publish to the tagged Release; the Linux upload runs after Windows so the Release already exists (avoids a
create-release race). **macOS** builds an **unsigned** artifact for testers only — Gatekeeper blocks
un-notarized apps, so public macOS distribution waits on an Apple Developer ID cert + notarization. Windows
Authenticode signing is optional and gated on the `WINDOWS_CERT_*` CI secrets (unsigned otherwise). Icons come
from one `assets/icon.svg` source rasterized by `scripts/icons.ps1` (generate-and-commit, not per-build).

### CLI + MCP distribution — .NET global tools via the existing NuGet (OIDC) flow
The `shelfbound` CLI and `shelfbound-mcp` server are `PackAsTool` packages published to NuGet by the existing
`nuget-publish.yml` (Trusted Publishing / OIDC, no stored key) on a `v*` tag — the same flow that ships the
`Shelfbound.*` libraries, so no separate pipeline. They keep independent `.csproj` versions (they mature at a
different rate than the tray). *Considered and rejected:* attaching nupkgs to GitHub Releases for manual
`--add-source` installs (worse UX than `dotnet tool install -g`) and a dedicated tools workflow (redundant —
`nuget-publish.yml` already packs every packable project).

---

## Open-source boundary (2026-07-02)

### Which code is public vs private — reviewed and recorded
Made the open-core boundary **deliberate rather than accidental** with an explicit pass over which currently-
private pieces could move public and which must never. Full three-bucket review + leak check in
[oss-boundary.md](./oss-boundary.md). The line: **public** = the local capability a user runs and audits plus
the snapshot interop contract (this repo); **private** = the hosted product's intelligence and economics (the
scoring engine, learned taste modelling, cross-source fusion, the enrichment fleet and its costs, accounts/
billing/hosted infra, and product strategy). Rule of thumb for new code: pure, local/snapshot/device-shaped,
no business or scoring value → public; the product's judgement, server economics, or strategy → private.
- **Candidates flagged to move public** (recommendations, not moves — each a separate future task): a
  conservative title-normalization utility and a pure install-history derivation (both snapshot-shaped, no
  moat). Device-fit evaluation is a **borderline** case recommended to **stay private** for now (coupled to a
  private type and a paid differentiator).
- **Hard rule (non-negotiable):** the hosted recommendation/scoring engine, learned taste/affinity modelling,
  cross-source enrichment fusion, server-side provider credentials/costs, server-side LLM usage, the hosted
  taste store, accounts/billing/hosted infra, buy/discovery/affiliate logic, and business/strategy docs stay
  private. This repo may acknowledge a private product exists (it does, openly) but never carry its internals.
- **Leak check: clean.** One low-severity follow-up — `mcp-design.md` links to a doc path inside the private
  repo; reword to drop the private-repo pointer. No secrets, pricing, or engine/fusion detail are present; the
  "local data is the moat" framing and the tray's `localhost` dev defaults are intended and safe.

---

## Tray — M-4 consequence: device management degrades to dashboard link (2026-07-02)

### Token-management endpoints require cookie session after M-4 — tray degrades gracefully

Cloud security fix **M-4** made `/auth/tokens*` (list devices, revoke, mint) reject **Bearer (API token)**
principals and require an interactive cookie session instead. The tray authenticates with a stored Bearer token,
so its `GET /auth/tokens` (device list) and `DELETE /auth/tokens/{id}` (revoke) calls would 403. These endpoints
are **only** the device-management ones — `GET /auth/me`, `GET /auth/entitlements`, and `POST /ingest` still
accept Bearer and are unaffected.

**Decision (implemented):**
- **Drop the server device list from the tray** — `GetDevicesAsync` and `RevokeDeviceAsync` removed from
  `ShelfboundClient`; `RefreshAccountAsync` only fetches account + entitlements. No 403s in the normal flow.
- **DEVICES panel → static + dashboard link** — shows this device's local name and a "Manage devices in dashboard"
  button (`WebAppUrl` from settings). Account/plan/sync display and the browser connect/mint onboarding flow are
  unchanged and still work over Bearer.
- **Sign-out is local-only** — clears the stored token immediately and stops auto-sync. Server-side revoke is not
  attempted (it would 403). Tokens expire naturally at 90 days or can be revoked from the web dashboard.
- **Dashboard device-management page is a follow-up** — the "Manage devices in dashboard" button currently links
  to the dashboard root (`WebAppUrl`). A direct `/devices` page waits on the deferred dashboard build
  (`TODO(dashboard)` in `MainWindow.axaml.cs`).

*Considered:* keeping a best-effort revoke call on sign-out (would swallow the 403 silently). Rejected — cleaner
to be explicit about local-only rather than pretend the revoke might succeed.

---

## Decky plugin — exploratory prototype (2026-07-03)

### Built as a thin controller on a prototype branch; hardware-gated, no public claims

The device plan sequences a Decky plugin only after the tray/CLI path is boringly reliable. This
prototype (`decky/`, branch `feat/decky-prototype`) de-risks that future build without waiting: a
runnable-in-theory plugin (React/TS quick-access panel + stdlib-Python backend) that emits the
standard snapshot contract. It has **never run on a real Steam Deck**; it exists to make the real
build cheap and the unknowns explicit — `decky/README.md` carries the needs-hardware validation list.

**Decisions (implemented):**

- **One producer contract, enforced by test.** The Python backend mirrors the C# scanner
  (VDF semantics, fallbacks, warning texts), and the emitted snapshot is validated against the
  **unmodified** `schema/snapshot.v0.schema.json` in an off-Deck pytest suite (44 tests). No forked
  data model, `source.tool: "shelfbound-decky"`.
- **Per-storage intelligence: UI-only in the prototype, now an additive contract field.** The
  prototype classified internal-SSD vs microSD + free space on-device only (`additionalProperties:
  false` rejected invented fields); library **paths** remain a local-only side channel and are never
  uploaded. The per-storage contract fields it anticipated landed additively in **v0.5.0** (kind +
  free/total, still no path) — see "Snapshot contract — per-storage on libraries" below.
- **Shared device identity.** The plugin reuses the CLI's `~/.config/Shelfbound/device-id`, so
  desktop-mode and Gaming-Mode syncs from the same Deck stay one device.
- **Pairing-code claim (proposed endpoints), not loopback OAuth.** `POST /devices/pair` +
  `/devices/pair/poll` are sketched in `cloud.py` and the plugin UI; against today's server it
  honestly reports "pairing not available" rather than faking success. The device token lives 0600
  in the Decky settings dir, never crosses to the frontend; disconnect is local-only (post-M-4
  posture, same as the tray).
- **No root, no background work, no runtime pip deps.** Scans/uploads are user-triggered only; the
  backend is auditable stdlib. The **modern-collections (leveldb) reader was deliberately cut** —
  legacy `sharedconfig.vdf` only, flagged by a snapshot warning — the biggest known gap, decided
  rather than hidden.

*Considered:* shelling out to the .NET CLI from the plugin instead of a native Python scan path (the
feasibility research weighs both). Deferred, not rejected — the prototype favors a self-contained
scan; the CLI shell-out gets a fair look once a real Deck exposes the actual runtime constraints.

---

## Snapshot contract — per-storage on libraries (2026-07-03)

### `libraries[].storage` added additively (v0.5.0) — kind + free/total, never a path
Storage medium + free space is device intelligence the hosted side can't see, and a wedge on two
devices: the **Steam Deck** (internal SSD vs microSD — "fits on your SD card", "free up space") and the
**PC** (internal vs external/network, for the tray). The decky prototype already classified storage but
kept it UI-only because `additionalProperties: false` rejected invented fields. Decision
(**implemented**): extend the snapshot **additively** with optional `libraries[].storage` =
`{ kind: internal|sdCard|external|network|unknown, freeBytes?, totalBytes? }`, bump
`SnapshotSchema.Version` 0.4.0 → **0.5.0** (minor/additive, `additionalProperties: false` preserved),
and have **both producers emit it**: the C# desktop scanner (`StorageClassifier` over
`DriveInfo.DriveType` + free/total per library path, covering CLI/tray/MCP through the shared
`SteamScanner`) and the decky plugin (`storage.py` mount-table classification), which now emits into the
contract and reads it back for the on-device panel **instead of classifying twice**. Old snapshots
without `storage` still validate; consumers stay lenient.

**No paths, ever** — kind + sizes only (a path would leak the filesystem layout; same rule the prototype
set). Where the OS can't tell the medium apart (SD-vs-USB on a desktop card reader, an unknown bus) the
producer emits `"unknown"` rather than guess. The decky privacy preview's "never uploaded" copy was
corrected accordingly (storage kind + free/total are now in the upload; mount points, device names, and
paths still are not).

*Follow-up (out of scope here):* the **hosted consumer** — cloud ingest reasoning over per-storage for
device-aware "fits on your SD card" / "free up space" recommendations — is a separate cloud task. Because
the change is additive, cloud ingest keeps working untouched until it opts in; the cloud repo's schema
copy just needs to follow the **0.5.0** bump.

---

## Recency correctness — newly-visible ≠ newly-added (2026-07-03)

### First-observation is "added" only under a stable scan scope; a scope expansion baselines, not dates
Steam exposes no purchase/added date, so Shelfbound infers "recently added" from when it **first observed
a game owned**, relative to a once-set baseline (`UserProfile.FirstScanAt`). That holds only while the
scan's *coverage* is stable. When coverage **widens** — an `installedOnly` baseline (no Steam Web API key)
then a `fullLibrary` scan — previously-owned games become visible for the first time *after* the baseline
and were falsely flagged "Added N days ago". This skewed the owner's own dev library: a stale narrow
baseline made a later full scan look full of "new" games.

Decision (**implemented**): the profile records the **widest scan scope observed so far**
(`UserProfile.WidestScanScope`, a high-water mark; `LibraryScope` is ordered by increasing coverage). A
scan broader than that mark **baselines** the games it reveals — stamps their first-seen at `FirstScanAt`
rather than "now" — so a scope expansion never fires recency. Games first seen under a stable-or-narrower
scope are genuine acquisitions and get the current timestamp; a real purchase after scope has stabilized
at full still reads "Added N days ago". Steam can't distinguish a purchase made *during* a scope change
from a pure reveal, so the rule errs conservative: **a missed novelty nudge beats a whole library falsely
flagged**. The fix is entirely upstream in the first-seen derivation (`UserDataActions.RecordFirstSeen`
plus the `SnapshotContext` ingest passing `stats.scope`); the consuming cloud signal (`RecentlyAddedSignal`,
`LibraryGame.AddedAgo != null && unplayed → +3`) is unchanged.

*Considered and rejected:* **freezing the first baseline's scope** instead of a high-water mark — every
later full scan would then read as "broader than baseline" and wrongly baseline genuine purchases forever,
breaking the real-acquisition case. **Gating recency at read time** on scope-stability — masks the symptom
in one view without fixing stored data and suppresses legitimate post-baseline acquisitions. The additive
`WidestScanScope` defaults to `installedOnly` (matching `SnapshotStats.Scope`), so a legacy profile
lacking it never treats a later full scan as a wave of real purchases. For an already-skewed profile,
`shelfbound profile --reset-recency` re-establishes the baseline from the current library (recency state
only; ratings/statuses/memories untouched).
