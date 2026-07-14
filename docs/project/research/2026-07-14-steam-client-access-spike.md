# Steam client access spike — 2026-07-14

## Outcome

This spike is a **no-go for automatic Steam Private filtering or first-class Steam Family labels from
the token-free artifacts tested**. It is a **go for a bounded privacy/access co-design slice** that can
settle a local exclusion boundary and stop treating an installed manifest as proof of ownership,
without claiming that boundary is synchronized from Steam.

The decisive Private-state result is not that VDF is authoritative. The installed client treats a
privileged account service as primary and persists a local VDF-backed cache. The cache followed local
and online refreshes, survived restarts, and supplied offline UI state; it also remained Private after
another client had changed the game to Public. It became current only when the desktop reconnected.
It therefore has useful positive evidence but no demonstrated completeness or freshness boundary.

The decisive Family result is similarly narrower than a domain model. A borrowed install wrote a
manifest whose `LastOwner` differed from the active account, while new own-preferred copies did not.
That disproves the assumption that `LastOwner` always identifies the local installing account, but it
does not prove current Family access, a lender, copy count, preferred copy, or revocation.

No production parser, dependency, snapshot field, upload behavior, or UI behavior was implemented.
The fixture names below are neutral labels; no real title, app id, account id/suffix, family-member
identity, credential, request URL, raw cache value, full file, or reversible fingerprint is retained.

## Question and authority standard

Shelfbound currently turns every installed manifest into a `games[]` entry, while downstream language
has treated those entries as owned. The experiment asked what the installed Steam client can reveal,
without credentials, about:

1. whether a game is currently marked Private by the active account; and
2. whether access is owned, borrowed from a Steam Family, or available from multiple copies.

The binding precedent is the collections incident: the legacy category VDF remained on disk years
after the modern UI had moved current state elsewhere. Storage format or location never establishes
authority. A candidate needs demonstrated account scope, precedence, refresh transitions, and
complete-empty versus failure semantics before it can guard a privacy boundary.

## Method and consent

The maintainer selected every fixture and performed every Steam action. The investigation only read
token-free, user-owned local files and statically inspected installed client UI code. It did not call
undocumented RPCs, extract a client token, inspect credential values, change Family membership,
change a preferred lender/copy, automate Steam, or contact Valve.

Fixtures exercised:

- `own-public-installed`: locally toggled Public → Private, later restored to Public from a second
  client while the observed desktop was offline.
- `own-private-installed` and `own-private-uninstalled`: passive known-Private controls.
- `borrowed-uninstalled` → `borrowed-installed`: Family access confirmed in the UI, then installed,
  launched, restarted online, and restarted offline.
- `dual-owned`: one uninstalled and one already-installed passive fixture, plus an own-preferred fresh
  install. The UI exposed “Change preferred copy”; it was not invoked.
- A second own-preferred fresh install lacked the Family-copy header. This is only a possible
  share-exclusion observation, not proof of an exclusion rule.

Read-only comparisons retained only membership booleans, counts/deltas, changed/unchanged state,
file-size deltas, and equality to the active account. Temporary diagnostics were excluded from Git and
removed after the evidence was reduced to this report.

## Private-state observations

### Static client behavior

Installed Steam UI code showed this chain:

1. `AccountPrivateApps.GetPrivateAppList#1` is the primary list request and is marked privileged.
2. A successful response rewrites a local object. On request failure, the UI uses that object's cached
   set when present; without a cached set it propagates failure.
3. A successful privacy toggle optimistically updates the same query/cache.
4. The object is serialized as JSON through `SteamClient.Storage.GetString/SetString`.
5. The local storage key resolves to
   `UserLocalConfigStore/WebStorage/PrivateApps_<accountId>` in `localconfig.vdf`; its observed value
   was a JSON integer array.

Static inspection establishes client intent and precedence, not successful response completeness at
an arbitrary later scan. The privileged RPC was not invoked by this spike.

### Dynamic transition

The `own-public-installed` transition produced these redacted observations:

| Transition | Steam UI | VDF fallback cache | Other candidates |
|---|---|---|---|
| Online Public → Private | Immediately Private | Membership appeared; aggregate count `+1` | No fixture-correlated change in the manifest, Chromium Local Storage, `licensecache`, `appinfo.vdf`, or `packageinfo.vdf` |
| Clean online restart | Private | Membership retained | No discriminating signal |
| Clean offline restart | Private | Membership retained | No discriminating signal |
| Reconnect without remote change | Private | Membership retained | No discriminating signal |
| Second client changes Private → Public while desktop is offline | Second client Public; offline desktop still Private | Stale Private membership retained | No discriminating signal |
| Desktop reconnects | Refreshed to Public in about one second | Membership disappeared; aggregate count `-1` | Manifests, Chromium Local Storage, `licensecache`, and `packageinfo.vdf` unchanged; small global `appinfo.vdf` churn was not fixture-specific |

The modern Chromium Local Storage databases, including cloud-storage namespaces, contained no
private-named key or private-list representation found by the existing bounded reader. Their exact
file count and total size did not change across the decisive transition. This absence is not evidence
that no other encoding or database exists; it only rejects the tested obvious representation.

### Private conclusion

- **Primary/current UI authority:** the privileged account service, when a request succeeds.
- **Demonstrated local persistence:** the per-account VDF object is an actively synchronized fallback
  cache, not an abandoned key.
- **Demonstrated failure mode:** the cache can be stale across clients while offline and carries no
  success, completeness, generation, expiry, or last-refresh marker.
- **Safe positive wording:** “Steam's local cache last marked this game Private.”
- **Unsafe wording:** “This is the current/complete set of Private games” or “cache absence means
  Public.”

A cached member can be used as a conservative hint or preselection, provided the product exposes its
uncertain freshness. An absent or empty cache cannot authorize upload under a promise to exclude all
Steam-Private games. What a background uploader does when freshness is unknown is a product decision,
not settled here; a preview control is only one possible interaction model.

## Steam Family observations

### Install, launch, restart, and offline

Before install, `borrowed-uninstalled` had UI-confirmed Family access and no manifest. Its newly created
manifest had `LastOwner != active account` from creation through install completion, launch, clean
online restart, and clean offline restart. The UI continued to show Family access and Play availability
offline without warning. Launch updated playtime/last-played presentation but did not change
`LastOwner`.

Every pre-existing installed manifest inspected at baseline had `LastOwner == active account`; the
known borrowed install was the sole new non-active value. The fresh own-preferred `dual-owned` install
and the second fresh owned install wrote `LastOwner == active account`. An already-installed
dual-owned fixture installed before Family creation also had an active-account value while the UI
offered “Change preferred copy.”

This is strong fixture-level evidence that manifest `LastOwner` can track the selected copy/license
owner rather than the person operating the installer. It remains ambiguous historical install/cache
evidence: another local account, a stale manifest, a later copy-preference change, or revoked access can
produce the same or inverse observations.

The offline UI result proves cached usability/presentation for that moment. It does not prove that a
current server-side grant still existed or how revocation would behave.

### Runtime-only and binary candidates

Installed UI code has richer runtime concepts: app overview data includes owner account and copy
count; app details include an owner, lock owner, different-copy flag, sharing-exclusion state, and
subscription/free-access state; Family UI code can obtain lenders and a preferred lender. The
underlying shared-library service is privileged. No stable token-free persistence for that full model
was demonstrated.

`licensecache` remained opaque binary input: no useful text header or relevant literal was found, and
its size did not change across the tested Family transitions. It was not copied, brute-forced, decoded,
or used to justify a field. `appinfo.vdf` and `packageinfo.vdf` knew about fixtures before installation
and behaved as global app/package metadata, not active-account grant evidence.

### Family conclusion

- **Safe wording for manifest comparison:** “The installed manifest's last-owner field differs from
  the active account.”
- **Unsafe wording:** “Borrowed from Family,” “owned by this member,” “currently accessible,” or
  “revoked.”
- Runtime UI proves Steam itself knows copy multiplicity/preference, but the tested token-free files do
  not expose a complete, current, supported grant model.
- Raw owner/member ids are third-party personal data. A future local diagnostic should reduce them to
  the minimum comparison/evidence needed and must not upload lender identity by default.

## Source and signal matrix

| Candidate | Exact fact observed | Active-account scope | Completeness/error semantics | Freshness | False positives / negatives | Privacy or credential risk | Stability confidence | Product-safe wording |
|---|---|---|---|---|---|---|---|---|
| Privileged Private-app service | Installed UI requests it first; success rewrites cache; failure falls back | Explicitly account-scoped in client flow | Success appears list-shaped, but completeness/empty contract is undocumented to Shelfbound | Current when the online request succeeds | Error or withheld response cannot be treated as empty | Requires privileged client context; invoking it may cross credential, support, and terms boundaries | Low as a third-party integration; high as observed UI intent | “Steam UI received the account's current Private list” only when Steam itself proves success |
| `localconfig.vdf` Private cache | Membership followed local toggle and reconnect; remained stale offline after remote Public change | Key suffix matched active account | No success/error/completeness marker; absent and empty are ambiguous | Last local update only | Stale positive proven; stale/missing negative remains possible | Token-free but contains sensitive per-account library membership; never emit raw value/suffix | Medium for cache behavior, low for durable contract | “Local cache last marked Private” |
| Chromium Local Storage LevelDB | No obvious Private representation or transition-correlated change found | Mixed namespaces; candidate user keys existed | Absence has no completeness meaning | File state only | Both directions unknown | Token-free but contains unrelated personal web state; bounded key/payload inspection only | Low for Private signal | No product claim |
| Manifest presence | Borrowed game appeared only after install | Per-device installation, not necessarily active account grant | Complete only for manifests found on this device; no uninstalled coverage | Install/cache lifecycle | Stale install, other account, temporary access, or removed grant | Token-free; title/app id are personal library data | High for install observation | “Installed on this device” |
| Manifest `LastOwner` comparison | Borrowed fixture differed; own-preferred fresh copies matched | Comparison uses active account, but field semantics are undocumented | No current-access, copy-count, or revocation contract | Last manifest write of unknown trigger | Other-account/stale/preference cases; active value does not prove ownership | Raw owner id may identify another person; compare in memory and discard | Medium for observed correlation, low for categorical use | “Manifest last-owner differs from active account” |
| Runtime Family UI | Family access, available Play, multiple-copy choice, and own preferred copy were visible | Active session | Rich UI state, but no supported scanner success/error contract | Current online; offline presentation can be cached | Offline/revoked and exclusion cases unresolved | Backed by privileged services and member ids; do not invoke/extract | Medium for concepts, low for integration | UI evidence only, not a snapshot claim |
| `licensecache` | Opaque file unchanged in tested transitions | Unknown | Unknown binary format | Unknown | Unknown | May contain license/identity material; no decoder or upload | None | No product claim |
| `appinfo.vdf` / `packageinfo.vdf` | Fixtures existed before install; only ambiguous global metadata churn | Global client cache | Not an account entitlement response | Cache-dependent | Metadata presence is a broad false positive for access | Token-free but broad; do not mine unrelated records | High for metadata, none for grant | “Client has metadata for this app” |
| `GetOwnedGames` through existing safe path | Two approved attempts found no configured key and made no request | Would target one account | This run produced no response; missing/failed response must not become empty owned set | Untested | Privacy settings, failure, and temporary access can all affect absence | API key is a credential; never log key or credential-bearing URL | Official API, but untested here for Private/Family | “Owned evidence returned by a successful call,” never infer from this run |
| `installed_without_owned_evidence` | Computable when an installed app lacks successful owned-set evidence | Depends on both observations | Explicitly an unknown bucket, not a negative or classification | Weakest input dominates | Conflates Private, borrowed, other-account, temporary, stale, and failed-response cases | Low if identifiers stay inside normal snapshot rules | High as a label for uncertainty only | “Installed without owned evidence” |

## Source-specific completeness rules

1. A Private list is complete-empty only after a documented, successful, current, account-scoped
   response says so. The VDF cache has no such success marker; missing or empty remains unknown.
2. Cache membership is evidence about the cache's last state, not proof of current remote truth.
3. A manifest is evidence of installation. `LastOwner` equality/inequality is a comparison signal only;
   neither value establishes own, Family, access, or revocation.
4. A successful owned-set response can add owned evidence within that API's documented visibility.
   No response, a missing `games` member, or an unconfigured key is not an empty owned set.
5. Missing LevelDB rows, unchanged opaque files, or absent text literals are not negative facts unless
   the source's scope and complete successful read have first been established.
6. UI state seen offline is cached presentation unless a current server round-trip is independently
   established.

## Tested and untested boundaries

Tested:

- local Public → Private and remote Private → Public transitions;
- immediate UI/cache behavior, clean online restart, clean offline restart, and reconnect refresh;
- installed and uninstalled known-Private passive controls;
- borrowed install, launch, online restart, offline restart, and UI play availability;
- dual-owned installed/uninstalled UI, own preferred copy, and fresh install;
- token-free manifest, LevelDB, VDF, `licensecache`, `appinfo.vdf`, and `packageinfo.vdf` comparisons.

Untested rather than simulated:

- successful direct `GetOwnedGames` response, because no key was configured;
- direct privileged `GetPrivateAppList`/toggle calls;
- a documented cache generation/expiry/completeness marker (none found);
- borrowed-uninstalled local persistence, grant revocation, Family removal, preferred-copy change,
  publisher exclusion, concurrent copy locks, DLC, temporary/free access, and regional changes;
- a maintained decoder/format contract for `licensecache`;
- whether another Steam client version/platform persists the same cache identically.

## Official and external interface check

Valve's documented Steamworks Family checks are scoped to the current running app or to a publisher
backend checking its own app; they are not a general consumer library-scanner API. `GetOwnedGames`
returns games owned by a player when their game details are visible and requires a Web API key. The
Private-app service observed in installed UI code is also present in SteamTracking's automatically
extracted WebUI protobuf and marked privileged, but it is not a documented public Steamworks method.

Calling that internal service is excluded for more than one reason: it would rely on ambient
authenticated client context or equivalent credential material, has no supported compatibility or
error contract, shares a privileged surface with mutation, and needs a separate terms/security/legal
decision. This report does **not** conclude that calling it is illegal; it concludes that it is outside
Shelfbound's no-credentials, supported-local-read boundary.

References:

- [Valve: Steam Families](https://partner.steamgames.com/doc/features/families?l=english&language=english)
- [Valve: ISteamApps](https://partner.steamgames.com/doc/api/ISteamApps?l=english)
- [Valve: IPlayerService / GetOwnedGames](https://partner.steamgames.com/doc/webapi/iplayerservice?language=english)
- [Valve: Web API key authentication](https://partner.steamgames.com/doc/webapi_overview/auth?l=english)
- [Valve: Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/index.html?l=english)
- [SteamTracking: extracted AccountPrivateApps WebUI service](https://github.com/SteamTracking/Protobufs/blob/master/webui/service_accountprivateapps.proto)

## Recommendation

### Next bounded slice: privacy/access semantics co-design

Proceed with one local-first contract/product co-design slice before more scanner parsing:

1. Settle a user-controlled local exclusion boundary that can prevent selected games from entering a
   hosted projection regardless of Steam cache availability. Candidate interactions include an
   account-level preference and explicit per-game exclusion; this report selects neither.
2. Decide the behavior of unattended/background upload when Steam-Private freshness is unknown. The
   candidates include blocking, warning/pausing, or requiring explicit exclusions; this spike does not
   choose the tray interaction or assume a preview step.
3. Specify future evidence/provenance semantics so installed manifests remain usable while no longer
   being described as owned. Keep `installed_without_owned_evidence` as uncertainty, not a category.
4. Treat cached-Private membership as optional conservative preselection only, with visible source and
   freshness limitations. Do not silently unexclude on absence/empty.
5. Define retention/removal behavior for a game previously uploaded and later excluded before claiming
   that local filtering solves already-hosted data.

Prerequisites are owner decisions on the background-sync failure posture and previously uploaded data,
plus cross-repository agreement on any future contract field. Tests should use synthetic identifiers
and explicit success/failure/empty/stale fixtures. No family-member identity should enter the hosted
contract.

### No-go boundaries

- Do not automatically promise “exclude games marked Private on Steam” from the VDF cache alone.
- Do not map manifest `LastOwner` into `OwnershipState`, platform, current Family access, or revocation.
- Do not use installed-minus-owned as Private or Family detection; name it
  `installed_without_owned_evidence`.
- Do not normalize extracting Steam session material or invoking privileged client RPCs.
- Do not add a `licensecache` decoder without maintained format evidence, a privacy review, and a
  separate dependency/design decision.

Further local crawling is unlikely to establish the missing authority contract. Automatic current
Steam-Private sync needs a documented/supported Valve interface or explicit Valve clarification; a
complete Family grant model likewise needs supported interface evidence. Maintained external format
research may explain binary candidates, but it cannot by itself prove completeness or permission to
use them. A future collections revalidation can be a separate regression check if client evidence
suggests its storage changed; this spike found no contradiction to the existing modern-LevelDB,
legacy-VDF-fallback decision.
