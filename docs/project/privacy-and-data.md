# Shelfbound — Privacy & Data (local core)

Trust is central: Shelfbound reads personal library data from local files. The product only works if
users believe — and can verify — that it is careful. This repo is open source partly so the local
data handling is **auditable**. This document covers the **open-source local tool**. (If you use a
separate hosted version, that service has its own privacy policy.)

## Principles

- Read only the local data that is needed; never arbitrary filesystem contents.
- Nothing leaves your machine. The scanner has no network access; it only writes a local snapshot
  file that you control.
- Never read or store Steam passwords/credentials, saves, or screenshots.
- AI-derived facts (via the local MCP server) carry source/evidence/confidence and are
  user-editable/deletable.
- Be careful with anything revealing: non-Steam game names, device names, usernames, paths.

## What is read locally (scanner)

- `steamapps/libraryfolders.vdf` — libraries and installed app ids.
- `steamapps/appmanifest_*.acf` — game name, install state, install-dir name, size, timestamps.
- `config/loginusers.vdf` — Steam accounts (id, login name, persona name).
- **Your categories/collections** — the modern Steam collections from the desktop client's local web
  storage (`htmlcache/Local Storage/leveldb`), falling back to the legacy
  `userdata/<id>/7/remote/sharedconfig.vdf`. Only your collection names + which games are in them are
  read — see [steam-collections.md](./steam-collections.md).
- Device/environment basics (machine name, OS) and a locally persisted random device id.
- Best-effort **hardware specs** (CPU, cores, RAM, GPU, OS, architecture) for device-aware
  recommendations — device facts only, **no serial numbers or fingerprints**, surfaced in the tray.

Nothing else. The scanner does not traverse user files, saves, or unrelated directories.

## What a snapshot contains vs deliberately excludes

The snapshot is built to be **safe-by-default even if you later choose to share/upload it** (see
[snapshot-schema.md](./snapshot-schema.md)):

**Included:** Steam account id(s), app ids, game names, installed yes/no, library index + label,
optional per-library storage kind (internal/SD/external/network) + free/total capacity, device
name/type/os + best-effort hardware specs, relative install-dir name, sizes/timestamps,
category/collection names, and a library-scope marker (installed-only vs full owned library).

**Excluded:** passwords/credentials, save files, screenshots, arbitrary files, **full install
paths**, library filesystem paths, mount points, storage-device names, install scripts, depot internals.

The device id is a **random GUID persisted locally** — not derived from hardware or account. The
Steam login `accountName` is included for local completeness; treat the snapshot file as personal.

## Memory/profile guardrails (local MCP write operations)

Do not let a model silently poison a user's durable profile. **Only persist explicit, user-stated
facts** ("I loved X because…", "mark this finished", "my `Deck` category means…"). Avoid storing weak
inferences ("user asked about horror once → likes horror"). Full rules and the write-tool design are
in [mcp-design.md](./mcp-design.md). Every stored memory has source, evidence, confidence, timestamps,
scope, and is visible/editable/deletable — surfaced today via `shelfbound profile` and the MCP
`get_remembered` tool (a richer review/edit view is still to come).
