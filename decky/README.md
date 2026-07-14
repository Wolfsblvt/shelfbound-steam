# Shelfbound — Decky plugin (prototype)

A [Decky Loader](https://decky.xyz/) plugin that makes Shelfbound feel first-class on a Steam Deck
in Gaming Mode: a quick-access panel with a **per-storage install view** (internal SSD vs microSD —
the signal desktop can't see), a **privacy preview** of exactly what a sync would upload, a manual
**Sync now**, and an account-claim flow via **pairing code** (proposed).

> **Status: exploratory prototype — hardware-gated.** Built and unit-tested off-Deck; it has
> **never run on a real Steam Deck**. Nothing here is a public claim of Deck support; per the
> project's device posture, public language stays "SteamOS devices" until real hardware validates
> the path. Not submitted to the Decky store. See
> [Needs a real Steam Deck](#needs-a-real-steam-deck-to-validate); the designed follow-up work is
> in [NEXT-STEPS.md](./NEXT-STEPS.md).

## What it is (and deliberately isn't)

This is a **thin controller** over the same open core everything else uses — a companion surface,
not a second scanner architecture:

- **One local producer contract, one hosted privacy boundary.** The backend emits the standard versioned snapshot
  ([`schema/snapshot.v0.schema.json`](../schema/snapshot.v0.schema.json), currently **0.6.0**) with
  `source.tool: "shelfbound-decky"`. Same shape the CLI/tray produce; no forked data model, no
  special ingest path. The scanner code deliberately mirrors the C# reference
  (`src/Shelfbound.Steam`) file-for-file: same VDF semantics (case-insensitive keys, last-wins),
  same fallbacks, same warning texts. Before network transport, `hosted_projection.py` reconstructs
  the same minimized whitelist as the C# client; the Python and C# serializers share one byte-exact
  golden fixture. Decky has no Web API enrichment, so it truthfully remains `installedOnly`; the
  contract's new `observedSubset` value is available to producers with positive non-complete sources.
- **Per-storage is a contract field; paths are not.** As of contract **v0.5.0** each library carries
  an optional `storage` object — medium kind (internal/sdCard/external/network/unknown) + free/total
  bytes — classified from the mount table and emitted like every other producer's storage data. The
  on-device panel reads that same field (classified once, not twice). Library **paths** stay a
  local-only side channel and are **never** uploaded — kind + sizes only.
- **One device identity.** The backend reuses the CLI's persisted random device id
  (`~/.config/Shelfbound/device-id`), so a Deck that synced from desktop mode and from Gaming Mode
  stays *one* device. The id is random, never derived from hardware.
- **No root** (the `plugin.json` flags deliberately omit it), **no background work** — every scan,
  preview, and upload is user-triggered. **No runtime pip dependencies** — the backend is pure
  stdlib, keeping the plugin auditable (Decky store review is audit-minded).

## Layout

```
decky/
  plugin.json               Decky metadata (api_version 1, no root flag)
  package.json              Frontend toolchain (pnpm 9, @decky/api + @decky/ui + @decky/rollup)
  main.py                   Backend entry: thin Plugin class, envelope responses, to_thread I/O
  decky.pyi                 Vendored loader typing stub (IDE support only)
  py_modules/shelfbound_decky/
    vdf.py                  Text VDF parser (mirrors src/Shelfbound.Steam/Vdf)
    steam_files.py          libraryfolders / appmanifest / loginusers / sharedconfig parsers + modern-collections JSON seam
    snappy.py               Snappy block decompression (mirrors Collections/Snappy.cs)
    chromium_leveldb.py     Chromium Local Storage LevelDB reader: *.ldb SSTables + *.log WAL (mirrors ChromiumLevelDb.cs)
    steam_collections.py    Modern collections facade: key build + value decode + JSON parse (mirrors SteamCollectionsReader.cs)
    steam_localstorage.py   Local Storage leveldb dir discovery (SteamOS candidate + env override; mirrors SteamLocalStorageLocator.cs)
    locator.py              Steam-root discovery (Linux/SteamOS candidates + env override)
    device_identity.py      Shared device-id, Deck detection, best-effort specs
    snapshot.py             Snapshot builder — the contract emitter (mirrors SteamScanner)
    storage.py              /proc/mounts parsing + storage classification → contract `storage` field
    overview.py             Per-storage panel view (reads the contract field; groups games, sizes, largest)
    hosted_projection.py    Whitelist-only hosted DTO + field-purpose manifest + canonical JSON
    privacy.py              Privacy preview payload (exact prepared upload body + summary)
    cloud.py                Typed /ingest outcomes, /auth/me, /auth/entitlements + PROPOSED pairing
    settings.py             Plugin settings + 0600 token store (Decky settings dir)
  src/                      React panel (@decky/ui): status, account, sync, storage, dev sections
  tests/                    pytest suite incl. schema validation — runs on any machine, no Deck
```

## Account claim: pairing code, not loopback

The tray's connect flow (localhost callback + browser redirect) is wrong for Gaming Mode: Decky
documents local-port conflicts, and browser-return into a controller-first UI is brittle. The
plugin instead sketches the **pairing-code / portal-claim** flow from the device plan:

1. `POST /devices/pair` → `{code, claimUrl, pollToken, expiresInSeconds}` — the panel shows the
   code (a QR of the claim URL is a planned nicety, e.g. `react-qr-code`).
2. The user opens the claim URL on any signed-in browser (phone/desktop) and enters the code.
3. The user presses **"I've entered the code"** — the plugin polls once per press
   (`POST /devices/pair/poll`) and stores the returned device token.

**These endpoints are a proposal** — the server doesn't implement them yet. Against today's server
the plugin reports "pairing not available" honestly instead of faking success. The token is stored
0600 in the Decky settings dir, never crosses to the frontend, and is only used as a Bearer for the
*live* endpoints (`/ingest`, `/auth/me`, `/auth/entitlements`). Disconnect is local-only; server-side
tokens expire at 90 days or are revoked from the dashboard (the post-M-4 posture, same as the tray).
Authenticated requests require HTTPS except for a literal loopback address. Redirects are followed
only within the exact same origin, so a bearer is never resent after a scheme, host, or port change.

## What is verified vs what is theory

**Verified off-Deck (runs in this repo, no hardware):**

- `pytest` suite: VDF parser semantics, every file parser against C#-mirrored
  expectations, the modern-collections reader (byte-level snappy vectors + a Chromium LevelDB WAL
  fixture + the collections-JSON parse seam, all mirroring the C# oracle tests), storage
  classification from fixture mount tables, device-id reuse/creation, the HTTP client's exact status
  mapping against a real local server, backend `Plugin` response envelopes/token boundary/pairing
  state transitions, typed ingest outcomes, one-use preview consent, and — the core checks — a
  Deck-shaped fixture scan whose **snapshot validates against the real, unmodified JSON Schema** plus
  Python hosted bytes that match the C# golden exactly.
- Frontend builds clean under strict TypeScript with the current `@decky/*` toolchain.

**Theory until a real Deck says otherwise:** everything in the next section.

## Needs a real Steam Deck to validate

1. **Loader runtime:** plugin loads under current Decky (api_version 1 → `Plugin()` instantiation,
   `py_modules` on `sys.path`, `decky` module env constants), on SteamOS's actual Python version.
2. **Steam root discovery** on real SteamOS (expected `~/.local/share/Steam`), including after
   SteamOS updates.
3. **SD-card mount patterns** across SteamOS versions — `/run/media/mmcblk0p1` vs
   `/run/media/deck/<label>`, exotic cases (dm-crypt, second card readers) — the classification
   heuristics in `storage.py` encode expectations, not observations. Free-space numbers should be
   sanity-checked against Settings → Storage.
4. **Device-id sharing in practice:** Gaming Mode's `$XDG_CONFIG_HOME`/`$HOME` for the plugin
   process vs the desktop-mode CLI/AppImage — the whole one-device story rides on both resolving
   to the same `~/.config/Shelfbound/device-id`.
5. **File realities on ext4:** casing of `config/loginusers.vdf` & friends, manifest field casing,
   VDF quirks in the wild.
6. **Quick-access UI:** section rendering, `ConfirmModal` with children, `TextField` + virtual
   keyboard inside a modal, scrolling the full exact JSON preview with a controller, and panel
   performance with 1000+ games.
7. **Network from the plugin backend** in Gaming Mode (the upload path), including against HTTPS.
8. **Suspend/resume** mid-scan and mid-upload.
9. **Update churn:** plugin surviving a SteamOS update / Decky reinstall cycle.
10. **Modern collections on real hardware:** confirm the SteamOS Local Storage leveldb path
    (`steam_localstorage.py` — the one hardware-TBD seam; env override + candidate provided) and
    that the ported reader reads current collections end-to-end on a modern-UI Deck. (The reader
    itself is Chromium/Steam-client format, not OS-specific, and is unit-tested off-Deck.)
11. **Pairing UX end-to-end** once the server endpoints exist: code entry on a phone, expiry
    handling, poll cadence.

## Known divergences from the C# scanner (deliberate prototype cuts)

- **Modern Steam collections are now read** (ported from the C# core): a stdlib snappy + Chromium
  LevelDB reader (`snappy.py`, `chromium_leveldb.py`, `steam_collections.py`) reads the current
  collections, preferring them and falling back to the legacy `sharedconfig.vdf` — a fallback that
  now warns **only when it actually happens**, instead of unconditionally. v1 reads static `added`
  lists only and skips dynamic `filterSpec` collections, exactly like the C# reader, and can lag
  the live client by the last unflushed edit. **[NEEDS-DECK]** the exact SteamOS Local Storage path
  is the one hardware-TBD seam (`steam_localstorage.py`; env override + candidate provided).
- **No Steam Web API enrichment** (visible not-installed observations + playtime): `stats.scope` is always
  `installedOnly`, `playtimeMinutes` never emitted. Enrichment likely stays a desktop/CLI concern.
- **GPU spec not collected** (C# uses the `Hardware.Info` library); other specs are best-effort
  from `/proc` and `uname`.
- **Locator** carries only the Linux/SteamOS candidate paths.

## Build, test, deploy (dev)

```bash
# Frontend (Node 22, pnpm 9 pinned in package.json via Corepack)
cd decky
corepack pnpm install --frozen-lockfile
corepack pnpm build          # → dist/index.js
corepack pnpm lint
corepack pnpm format:check

# Backend tests (any OS, no Deck needed)
python -m venv .venv
.venv/bin/python -m pip install -r requirements-dev.txt   # .venv\Scripts\python on Windows
.venv/bin/python -m pytest tests                          # use the Windows path there too
```

Deploying to a Deck is manual/dev-only for now (no store submission): the [Decky CLI /
plugin-template tooling](https://github.com/SteamDeckHomebrew/decky-plugin-template) builds and
ships a plugin folder containing `plugin.json`, `package.json`, `dist/`, `main.py`, and
`py_modules/` to `/home/deck/homebrew/plugins/<name>` over SSH. **[NEEDS-DECK]** the exact
packaging/deploy loop is unverified from this monorepo subdirectory.

## Privacy

Same hard rules as the core ([privacy-and-data.md](../docs/project/privacy-and-data.md)): only Steam
library metadata is read. Preview creates one minimized, compact hosted body plus a one-use id;
confirmation sends those exact bytes, while stale/reused ids fail closed. The upload drops the entire
Steam-account array, automatic hostname, exact OS build, filesystem paths, credentials, saves,
screenshots, and serials. It includes the user/neutral device label, random device id, coarse OS/specs,
games, collections, and per-library storage kind + free/total capacity. Storage paths, mount points,
and block-device names stay local. Game and collection names remain personal. License:
AGPL-3.0-or-later (whole repo).
