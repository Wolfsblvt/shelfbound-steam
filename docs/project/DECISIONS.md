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

### Privacy-separated local snapshot and hosted projection (updated 2026-07-11)
The complete local snapshot remains useful to local consumers and includes Steam identity detail. It
still omits full filesystem paths (libraries → index + label; games → relative install-dir only), uses
a random non-hardware `device.id`, and never represents credentials, saves, screenshots, serials, or
arbitrary files — but it is personal, not an upload-safe anonymous blob.

Official hosted clients must pass it through **projection v1** in `Shelfbound.Client`: a dedicated,
whitelist-only DTO graph shared by CLI + tray. The projection drops `steamAccounts` entirely, prevents
an automatic hostname label, coarsens exact OS builds, and retains product-justified device/library/
game/category/stats fields with a leaf-by-leaf purpose manifest. Preview and transport share one
prepared compact JSON body; tray background consent is versioned. Decky's Python mirror is pinned to
the same byte-exact golden fixture. *Rejected:* redacting at the receiver (the data has already crossed
the trust boundary), serializing the local model with ignored properties (new nested fields could leak),
or maintaining separate CLI/tray payload builders (drift). See [privacy-and-data.md](./privacy-and-data.md).

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
(`STEAM_WEB_API_KEY` or the saved local config); secret values are never accepted in argv.

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
both the CLI and the MCP server (key precedence: env > config). Setup reads the key from stdin or the
environment, never argv. The key is the user's own read-only, rate-limited credential, stored plaintext
for now; config and personal-profile writes are atomic and mode 0600 on Unix, while Windows files
inherit the current user's profile-directory ACL. OS-keystore encryption (DPAPI / libsecret) remains a
planned hardening. Never in the repo, never logged.

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
- **Modern dynamic collections** (leveldb) to complement the legacy categories already parsed.

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

### CLI + MCP distribution — .NET global tools with independent release identities
The `shelfbound` CLI and `shelfbound-mcp` server are `PackAsTool` packages on NuGet and keep independent
`.csproj` versions (they mature at a different rate than the tray and libraries). The immutable library
`v*` stream now packs **only** `Shelfbound.Core/Query/Steam`; otherwise an unchanged tool version would
either make a library release fail as a duplicate or require the unsafe `--skip-duplicate` behavior.
The next tool update therefore gets a project-scoped tag/workflow rather than hitching a ride on a library
tag. *Rejected:* attaching nupkgs to GitHub Releases for manual `--add-source` installs (worse UX than
`dotnet tool install -g`) and continuing the mixed publish set with silent duplicate skips.

### Library package identity — immutable version/schema/commit, gated before publish
The current library release is **package `0.7.0` carrying snapshot schema `0.5.0`**. Published `0.6.0`
remains its historical schema-`0.4.0` payload forever; an immutable version is never repacked. The .NET SDK's
built-in Source Link support emits portable symbols and the exact repository commit, while the nuspec release
notes embed the schema mapping. CI packs and inspects all three libraries, compares schema/package changes to
the previous `v*` release, and runs SDK package validation against the previous published package.

API breaks are fail-closed. Before 1.0, an intentional break requires a minor package bump plus a reviewed,
target-specific APICompat suppression; package `0.7.0` records the existing three-argument →
four-argument `RecordFirstSeen` break explicitly. A new suppression on a patch/same version fails policy.
NuGet publishing preflights that the version is absent and pushes without `--skip-duplicate`, so a race or
partial prior publish also fails visibly.

The published library set stays **Core, Query, Steam**. `Shelfbound.Storage` is deliberately not a package:
it owns local config and user-data persistence, not the portable snapshot boundary. The similarly named
`SnapshotStorage` contract DTO lives in `Shelfbound.Core`, so storage facts round-trip without exporting the
local persistence assembly.

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

## Tray — one-time-code connect and upload-only authority (2026-07-11)

### Browser onboarding never carries a bearer; the connected tray cannot read account data

The earlier M-4 hardening moved token inventory and revocation behind an interactive cookie session. SEC-01
then removed the broader flaw in native onboarding: the dashboard must not place a 90-day bearer in loopback
browser navigation. The tray is an uploader, so retaining account/library read authority merely to populate a
richer card would violate least privilege.

**Decision (implemented):**
- **One-time-code handshake** — the tray opens the dashboard with an exact numeric-loopback redirect, an explicit
  trimmed device name, and cryptographically random state. The callback contains only `code` + exact `state`.
  The tray redeems that code once, out of band and without Authorization/cookies, then stores the returned token.
  Literal callback spelling is validated before URL parsing; parser-normalized aliases are never trusted.
- **Upload-only, device-bound token** — redemption accepts only the exact `device:upload` scope and bound device
  name. The same normalized name is emitted in the first and every later snapshot upload. Browser URLs, callback
  records, and logs never contain the bearer.
- **Account card deliberately degrades** — it shows the bound device name and `Connected (upload-only)`. The tray
  no longer calls `/auth/me`, `/auth/entitlements`, `/library`, MCP, or token-management endpoints with this token,
  avoiding expected 401/403 noise and any temptation to widen its authority for presentation data.
- **Device management stays in the dashboard** — the tray keeps the static "Manage devices in dashboard" link.
  Sign-out remains local-only: it clears the stored token and stops auto-sync; expiry/revocation is server-side.
- **Existing token store retained** — DPAPI protects the token on Windows and a mode-0600 file is used elsewhere.
  Keychain/libsecret integration remains a follow-up; the residual non-Windows weakness is materially constrained
  now that the stored credential can only upload for one bound device.

*Considered and rejected:* retaining or minting a broad Bearer so the tray could continue showing display name,
plan, and allowance. That trades a cosmetic account card for account/library authority on a background uploader.
A future richer account view needs a separate interactive read session, not broader device-token scope.

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
  **unmodified** `schema/snapshot.v0.schema.json` in the off-Deck pytest suite. No forked
  data model, `source.tool: "shelfbound-decky"`. Hosted transport then mirrors the C# hosted
  projection rather than sending that complete local document.
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
  backend is auditable stdlib. The modern-collections (leveldb) reader was initially cut (legacy
  `sharedconfig.vdf` only + a snapshot warning) — **since ported to stdlib Python** (see the
  modern-collections entry below), so the plugin now reads current collections like the C# core.

*Considered:* shelling out to the .NET CLI from the plugin instead of a native Python scan path (the
feasibility research weighs both). Initially deferred; **now resolved in favor of the self-contained
port** for the categories reader (see the next entry) — it keeps the plugin auditable with no runtime
deps. A CLI shell-out remains a fallback idea if hardware later argues for it.

### Modern collections — ported to stdlib Python (prefer-modern, fallback-legacy)

The prototype's biggest known gap (modern collections cut; legacy `sharedconfig.vdf` only + a warning)
is **resolved**: the decky backend ports the C# `Shelfbound.Steam.Collections` reader to
dependency-free Python (`snappy.py`, `chromium_leveldb.py`, `steam_collections.py`,
`steam_localstorage.py`), faithfully mirroring its semantics and oracle tests — prefer modern
collections, fall back to the legacy VDF (**warning only on an actual fallback**, no longer
unconditional), static `added` lists only, skip dynamic `filterSpec`, tolerate a malformed collection,
accept lag-by-last-unflushed-edit. This **mirrors, and does not re-decide,** the core "Modern
collections — hand-rolled Chromium-leveldb reader" decision above. *Chosen over A2's other arm* —
shelling out to the .NET CLI where installed (reuses the battle-tested reader but adds a runtime
dependency + Decky review surface); the self-contained stdlib port keeps the plugin auditable, no
runtime pip deps. **One hardware-TBD seam:** the SteamOS Local Storage leveldb *path* (env override
`SHELFBOUND_STEAM_LOCALSTORAGE` + a candidate path; validated on a real Deck under A1). Dynamic
`filterSpec` collections remain a later item. See `decky/NEXT-STEPS.md` §A2 and
[steam-collections.md](./steam-collections.md).

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
producer emits `"unknown"` rather than guess. The Decky privacy preview's "never uploaded" copy was
corrected accordingly (storage kind + free/total are in the hosted projection; mount points, storage
device names, and paths are not).

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
