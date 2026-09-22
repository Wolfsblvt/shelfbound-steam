# Decky plugin — designed next steps (handoff)

The prototype is signed off at `signoff/decky-prototype` (runnable in theory, never on hardware).
This is the designed sequel — **ordered by dependency, not ambition**. Read together with the
[README](./README.md)'s "Needs a real Steam Deck" list; nothing in Phase B/C starts before Phase A
has produced facts.

## Phase A — first hardware pass (turn theory into facts)

**A1. Run the validation list on a real Deck** (README §Needs a real Steam Deck) and record the
results here. Everything below is re-scoped by what this finds — especially SD mount patterns,
Gaming-Mode env, QAM UI behavior, and the modern-collections Local Storage leveldb path (README
item 10 — the one hardware-TBD seam left by the A2 port; `steam_localstorage.py` has the candidate
+ `SHELFBOUND_STEAM_LOCALSTORAGE` override, so it should be a one-line confirm).

**A2. Categories: ported the modern-collections reader to Python.** ✅ The staleness was already
proven (Slay the Princess → wrong via the legacy VDF, `Finished` via modern), so this skipped the
measure step and took the **port** arm: a stdlib snappy + Chromium LevelDB reader
(`decky/py_modules/shelfbound_decky/`: `snappy.py`, `chromium_leveldb.py`, `steam_collections.py`,
`steam_localstorage.py`) mirroring the C# `Shelfbound.Steam.Collections` reader and its oracle
tests — no runtime pip deps. The scanner now **prefers modern collections and falls back to the
legacy `sharedconfig.vdf`**, warning only on an actual fallback (not unconditionally). Static
`added` lists only; dynamic `filterSpec` collections skipped (v1 parity with C#); can lag the live
client by the last unflushed edit.

*Left for A1 (hardware):* the one hardware-TBD seam is the SteamOS Local Storage leveldb path — the
env override + candidate path make it testable off-Deck and a one-line fix on real hardware.
*Deliberately still out of scope:* dynamic `filterSpec` collections and the CLI shell-out
alternative. See the DECISIONS entry and docs/project/steam-collections.md.

**A3. Surface entitlements in the panel.** `cloud.py` already has `get_entitlements()`
(plan, autoSync, minUploadIntervalSeconds, maxDevices) — it's just not wired into the UI. Show
plan + device allowance in the Account section (the tray already does this), and use
`retryAfterSeconds` from a throttled sync for a "next sync possible in …" hint. Keep the posture:
**server enforces, client only displays.**

**A4. Pairing proposal v2 — failure semantics.** The proposed `POST /devices/pair(/poll)` contract
needs explicit denial reasons before the server implements it:
- `{status: "denied", reason: "deviceLimit" | "userDenied" | …}` so the panel can say *"your plan's
  device allowance is used — manage devices in the dashboard"* instead of a bare "denied".
- The claim page (browser side) is where the user can actually fix a limit — surface it there first.
- **Open question for the hosted side:** device counting must key on the persisted **device id**,
  not on tokens — a Deck that pairs from Gaming Mode *and* connects the tray in desktop mode shares
  one `~/.config/Shelfbound/device-id` but holds two tokens; it must count as **one** device.

**A5. Backend Plugin-class tests — DONE.** `main.py` imports off-loader through a stubbed
`decky` module in pytest, covering the envelope behavior, token-never-in-frontend invariant, and
pairing state transitions.

## Phase B — contract evolution (cross-producer, coordinate with the hosted side)

**B1. Per-storage as contract data — DONE (v0.5.0).** Shipped additively: optional `libraries[].storage`
= `{ kind: internal|sdCard|external|network|unknown, freeBytes?, totalBytes? }`, emitted by both
producers (C# `StorageClassifier` via `DriveInfo`; decky `storage.py` via the mount table, read back by
the on-device panel instead of classifying twice). **No paths, ever** — kind + sizes only. See the
DECISIONS entry "Snapshot contract — per-storage on libraries" and `docs/project/snapshot-schema.md`.
*Remaining follow-up (separate cloud task):* the **hosted consumer** that ingests per-storage for
device-aware "fits on your SD card" / "free up space" views — additive, so cloud ingest keeps working
until it opts into the 0.5.0 field.

## Phase C — product polish (post-validation; order flexible)

- **QR code** for the claim URL (`react-qr-code` — tiny, MIT) once a real pairing server exists.
- **Toasts + events**: `decky.emit` sync progress/completion, `toaster` on success/throttle.
- **Travel checklist card** (paused/unfinished games + SD free space) — built ONLY on the open
  local heuristics (`Shelfbound.Query` ports); hosted scoring stays out of the plugin.
- **Localization** via Decky's i18n once strings settle.
- **Packaging/store decision** per the feasibility gates: manual/GitHub install first; Decky store
  only if the free/local plugin stands alone and everything stays auditable.
- **Auto-sync while Gaming Mode is active** — deliberately last; user-triggered stays the default
  until update-churn behavior is understood on hardware.

## Non-goals until hardware validates

No store submission, no public Deck-support claims (SteamOS-device language only), no auto-sync,
no root, no runtime pip deps.
