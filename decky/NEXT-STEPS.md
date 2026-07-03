# Decky plugin — designed next steps (handoff)

The prototype is signed off at `signoff/decky-prototype` (runnable in theory, never on hardware).
This is the designed sequel — **ordered by dependency, not ambition**. Read together with the
[README](./README.md)'s "Needs a real Steam Deck" list; nothing in Phase B/C starts before Phase A
has produced facts.

## Phase A — first hardware pass (turn theory into facts)

**A1. Run the validation list on a real Deck** (README §Needs a real Steam Deck) and record the
results here. Everything below is re-scoped by what this finds — especially SD mount patterns,
Gaming-Mode env, and QAM UI behavior.

**A2. Categories: measure, then decide.** On a modern-UI Deck, check how stale the legacy
`sharedconfig.vdf` really is (expected: very). Then pick one:
- *Port the modern-collections reader to Python* — the C# reader
  (`src/Shelfbound.Steam/Collections`: hand-rolled snappy + leveldb + the Chromium Local Storage
  key format) is small and dependency-free, so a stdlib Python port is feasible and keeps the
  plugin self-contained.
- *Shell out to the CLI/agent where installed* — reuses the battle-tested reader, but adds a
  runtime dependency and review surface (the feasibility research weighs this).
Decision criteria: does Steam-running file locking bite either path; SteamOS Python behavior;
Decky review friendliness. Until then the snapshot warning stays.

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

**A5. Backend Plugin-class tests.** `main.py` is thin but untested (it imports the loader-injected
`decky` module). Inject a stub `decky` into `sys.modules` in a test fixture and cover the envelope
behavior, token-never-in-frontend invariant, and pairing state transitions.

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
