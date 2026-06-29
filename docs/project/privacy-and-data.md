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
- AI-derived facts (when the local MCP server lands) carry source/evidence/confidence and are
  user-editable/deletable.
- Be careful with anything revealing: non-Steam game names, device names, usernames, paths.

## What is read locally (v0 scanner)

- `steamapps/libraryfolders.vdf` — libraries and installed app ids.
- `steamapps/appmanifest_*.acf` — game name, install state, install-dir name, size, timestamps.
- `config/loginusers.vdf` — Steam accounts (id, login name, persona name).
- Device/environment basics (machine name, OS) and a locally persisted random device id.
- Best-effort **hardware specs** (CPU, cores, RAM, GPU, OS, architecture) for device-aware
  recommendations — device facts only, **no serial numbers or fingerprints**, surfaced in the tray.

Nothing else. The scanner does not traverse user files, saves, or unrelated directories.

## What a snapshot contains vs deliberately excludes

The snapshot is built to be **safe-by-default even if you later choose to share/upload it** (see
[snapshot-schema.md](./snapshot-schema.md)):

**Included:** Steam account id(s), app ids, game names, installed yes/no, library index + label,
device name/type/os, relative install-dir name, sizes/timestamps. (`categories`, when implemented.)

**Excluded:** passwords/credentials, save files, screenshots, arbitrary files, **full install
paths**, library filesystem paths, install scripts, depot internals.

The device id is a **random GUID persisted locally** — not derived from hardware or account. The
Steam login `accountName` is included for local completeness; treat the snapshot file as personal.

## Memory/profile guardrails (local MCP write operations, when built)

Do not let a model silently poison a user's durable profile. **Only persist explicit, user-stated
facts** ("I loved X because…", "mark this finished", "my `Deck` category means…"). Avoid storing weak
inferences ("user asked about horror once → likes horror"). Full rules and the write-tool design are
in [mcp-design.md](./mcp-design.md). Every stored memory has source, evidence, confidence, timestamps,
scope, and is visible/editable/deletable via a future "what Shelfbound remembers" view.
