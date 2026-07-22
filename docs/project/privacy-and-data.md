# Shelfbound — Privacy & Data (local core)

Trust is central: Shelfbound reads personal library data from local files. This repository is open
source partly so both the local reads and the official hosted-upload boundary are auditable. A
separate hosted service has its own privacy policy, retention rules, and account controls.

## Principles

- Read only the local data that is needed; never arbitrary filesystem contents.
- The scanner itself has no network access. Nothing leaves unless the user explicitly exports or
  invokes an upload-capable client.
- Official clients upload a separate, whitelist-only **hosted projection**, never the complete local
  `SnapshotDocument`.
- Never read or store Steam passwords/credentials, saves, screenshots, hardware serials, or MACs.
- AI-derived local facts carry source/evidence/confidence and are user-editable/deletable.
- Be honest about revealing data: game and collection names, a device label, specs, and play history
  are personal even when they are useful product data.

## What is read locally

- `steamapps/libraryfolders.vdf` — libraries and installed app ids.
- `steamapps/appmanifest_*.acf` — game name, install state, relative install-dir name, size, timestamps.
- `config/loginusers.vdf` — Steam accounts (id, login name, persona name).
- **Your categories/collections** — modern Steam collections from the desktop client's local web
  storage (`htmlcache/Local Storage/leveldb`), falling back to the legacy
  `userdata/<id>/7/remote/sharedconfig.vdf`. Only collection names + game membership are read; see
  [steam-collections.md](./steam-collections.md).
- When the user enables **“Don't sync games marked Private in Steam”**, Shelfbound reads the account-scoped
  `userdata/<id>/config/localconfig.vdf` value at
  `UserLocalConfigStore/WebStorage/PrivateApps_<accountId>`. A bounded path-selective reader discards
  unrelated VDF scalar contents while scanning; only the expected JSON integer array is retained and
  parsed. Its positive membership is used in memory for hosted omission; the key suffix, account id,
  raw value, and evidence outcomes are not uploaded or generally logged.
- With an optional user-provided Steam Web API key, positive visibility-gated game/playtime
  observations. The response is never treated as complete; missing, empty, and malformed results warn
  and add no remote rows. The key is sent only to Steam and is never written into a snapshot or warning.
- A device label, OS, and a locally persisted random device id. Upload-capable scanner paths use a
  user choice or the neutral default `Shelfbound device`; a local-only producer may retain more local
  detail, which is why the hosted projection also neutralizes the current machine hostname.
- Best-effort hardware specs (CPU, cores, RAM, GPU, OS description, architecture) for device-aware
  recommendations. No serial numbers or hardware-derived ids are collected.

Nothing else. The scanner does not traverse saves, unrelated user files, or arbitrary directories.

## Steam access-state research boundary

The portable scanner still does not derive or emit Steam Private or Steam Family state. Official
upload-capable clients now implement the narrower positive-only Private-game boundary established by the
redacted local [evidence spike](./research/2026-07-14-steam-client-access-spike.md):

- `UserLocalConfigStore/WebStorage/PrivateApps_<accountId>` in `localconfig.vdf` is a per-account,
  VDF-backed fallback cache used by the current client. It can be stale while offline and has no proven
  freshness/completeness marker. Its raw suffix and value are personal library/identity data and must
  not be logged or uploaded.
- Manifest presence proves installation only. `LastOwner` can differ from the active account for a
  known borrowed install, but the raw value may identify another person and does not establish current
  Family access. Future diagnostics should compare in memory, discard the ids, and retain only the
  minimum qualified evidence.
- `SharedAuth.AuthData`, cookies, login/session/access tokens, token-bearing request URLs, and
  equivalent Steam client credential material are forbidden inputs. A separately user-provided Steam
  Web API key may be used only through the existing protected API path; never inspect or log its value
  or a credential-bearing URL. Do not invoke privileged Steam client RPCs or turn ambient authenticated
  client state into a scanner dependency.
- Missing, empty, stale, withheld, offline, and failed states remain distinct until a source supplies a
  documented successful-completeness signal. A privacy boundary must not reinterpret any of them as
  “no Private games” or “not borrowed.”

No additional cache/binary candidate is read; manifest `LastOwner` is not extracted or emitted.

## Local input and storage hardening

- Steam VDF and Chromium LevelDB inputs have explicit file, nesting, decoded-output, entry-count, and
  offset/length bounds. Malformed cache fragments fail closed and the scanner falls back or reports a
  warning instead of allocating from attacker-controlled lengths.
- Manifest `installdir` is emitted only when it is one relative folder name. Absolute paths, traversal
  segments, separators, drive-qualified names, and oversized values are omitted before snapshot or
  upload construction.
- The Steam API config and personal profile JSON are atomically created as mode 0600 on Unix. Windows
  relies on the current user's profile-directory ACL; the tray bearer token additionally uses DPAPI.
  Decky settings, device id, and token files are mode 0600 on SteamOS.
- Steam API keys and Shelfbound bearer tokens are accepted through environment, stdin, or protected
  files—not command-line values that leak through shell history and process argument inspection.

## The complete local snapshot is personal

The portable snapshot contract contains the complete local data needed by local consumers:

- Steam account ids, login/persona names, app ids, game names, installed state, playtime, sizes and
  timestamps;
- library index + label, optional storage kind + free/total capacity, and relative install-dir names;
- the device label/type/OS/specs and random device id;
- category/collection names and the scan-scope marker.

It deliberately has no passwords, credentials, saves, screenshots, full install/library paths,
mount points, storage-device names, hardware serials, or MACs. That makes the local file safer, but
**not anonymous and not the hosted upload body**. It contains Steam identity fields and should be
treated as personal data. See [snapshot-schema.md](./snapshot-schema.md).

## Hosted upload projection v2

`Shelfbound.Client.HostedProjection` is the C# source of truth used by the CLI and tray. Decky's
`hosted_projection.py` mirrors the same whitelist and is pinned to the same cross-language golden
fixture. Each implementation carries an explicit leaf-by-leaf field-purpose manifest.

The hosted body includes:

- snapshot version/id/capture time and producer provenance;
- random `device.id`, the **user-chosen or neutral** `device.name`, type and OS family;
- CPU/GPU/core/RAM/architecture specs and a **coarsened** OS description (`Windows 10/11`, `Linux`,
  or `macOS`, never an exact build/kernel string);
- libraries (index, label, count, optional storage kind/free/total), games, categories, and stats,
  including whether coverage is installed-only, an observed non-complete subset, or complete under an
  explicit source contract.

It drops the complete `steamAccounts` array: `steamId64`, `accountName`, `personaName`, and
`mostRecent` do not leave the machine. Account ownership comes from the authenticated upload token,
not from snapshot identity fields. A legacy snapshot whose device label equals the current machine
hostname is also replaced with `Shelfbound device` before transport.

The product data remains personal. In particular, `games[].name` can reveal a private/non-Steam title
from a future or third-party producer (official producers are Steam-only today), and collection names
can reveal the user's own vocabulary. Hardware model combinations can also be distinctive even though
they contain no serials; they are included specifically for device-fit recommendations.

The projection is fail-closed: invalid input produces no request. It reconstructs every nested object
instead of serializing local model objects directly, so a future local field cannot silently start
uploading.

With Private-game exclusion enabled, only positive membership from the local account caches can omit a
game, and a device-local un-skip wins. Absent, empty, unreadable, malformed, or mismatched evidence omits
nothing and produces a visible best-effort status. The filter changes only this projection: neither the
portable snapshot nor local MCP/library behavior changes. Omitted rows leave no marker, reason, evidence,
or count in the hosted body. Affected library/category/stats aggregates are rebuilt from retained rows;
any actual omission changes `fullLibrary` to `observedSubset` but never rewrites an already-partial scope.

## Preview and consent

- `shelfbound upload --dry-run` prints the exact compact hosted body to stdout and sends nothing. It
  needs neither a server URL nor token.
- The tray builds one prepared body, displays its exact JSON, and uploads that same object only after
  confirmation. New installs default background sync off. Background sync cannot start until a user
  has previewed and successfully sent the current projection version; a future field-set expansion or
  material field-purpose change invalidates that consent. Projection v2 renews consent for the changed
  `stats.scope` purpose without adding a new uploaded field. Legacy documents retain their published
  scope label and schema identity in the preview/body; consumers apply the compatibility normalization.
- The Private-game setting itself defaults off. Changing it clears Tray background consent so the next
  upload is previewed. Preview lists only the local titles that would be skipped and can persist a
  device-local **Sync this game** override; losing that settings file merely removes the override, causing
  positive evidence to omit again. Decky uses the same one-use prepared-body rule and is manual-only.
- Before any tray hosted action, the user must explicitly save this device's type. The existing `device.type`
  field and its purpose are unchanged: Desktop, Laptop, Steam Deck, and Other / not sure (explicit `unknown`) are
  valid choices; a Steam Deck suggestion still requires confirmation. This is local setup truth, not a new
  projected field or a consent-version change.
- Decky returns one prepared body plus a one-use upload id. Confirming sends those exact bytes; a
  stale/reused id is rejected and requires a fresh preview.

The CLI continues to use the same hosted projection, but it has no established protected interactive
settings seam for this policy or per-game overrides. Its Private-game policy therefore remains default-off;
users relying on the Tray/Decky setting should not use CLI upload as an equivalent enabled path. A future
CLI slice must reuse `PrivateGameUploadPreparer` and protected local configuration rather than app ids in
arguments or shell history.

## Memory/profile guardrails (local MCP write operations)

Do not let a model silently poison a user's durable profile. **Only persist explicit, user-stated
facts** ("I loved X because…", "mark this finished", "my `Deck` category means…"). Avoid weak
inferences ("user asked about horror once → likes horror"). Full rules and the write-tool design are
in [mcp-design.md](./mcp-design.md). Every stored memory has source, evidence, confidence, timestamps,
scope, and is visible/editable/deletable via `shelfbound profile` and the MCP `get_remembered` tool.
