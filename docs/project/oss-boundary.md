# Shelfbound — Open-Source Boundary (open-core core)

> What belongs in this public repo and what stays in the separate private product, made **deliberate
> rather than accidental**. This is a decision/reference doc; it does not move any code. Public-safe by
> construction — it names public capabilities and describes private ones only by category, never their
> internals. Status as of 2026-07-02.

Shelfbound is **open-core**: this repository (`shelfbound-steam`, AGPL-3.0-or-later) is the free local
core; a separate private repository holds the proprietary hosted product that funds the project. See
[DECISIONS.md](./DECISIONS.md) ("Two repositories", "License") and [ARCHITECTURE.md](./ARCHITECTURE.md)
("Repository boundary"). The two interoperate through the **snapshot contract**, and the hosted product
additionally reuses the public libraries as packages under the [CLA](../../CLA.md).

## The principle

The line is drawn so it is easy to apply to any new piece of code:

- **Public** — the local capability a user runs and audits, plus the interoperability contract:
  local Steam reading, the snapshot format, the local query/merge/profile logic, local storage, the
  CLI, the tray, and the local MCP server. This is the differentiator; keeping it open (and AGPL) is
  what makes Shelfbound trustworthy and prevents a third party from quietly closing the free core.
- **Private** — the *intelligence and economics* layered on top: the hosted recommendation/scoring
  engine, learned taste modelling, cross-source fusion, the enrichment provider fleet and its costs,
  accounts/billing/hosted infrastructure, and product strategy. Public history is forever, so anything
  strategic must never land here.

Rule of thumb for a new file: **pure, local/snapshot/device-shaped, no business or scoring value →
public; anything that is the product's judgement, its server economics, or its strategy → private.**

The **Permanently private** list below is a hard rule, not a starting position for negotiation. This
doc confirms the line; it does not reopen it.

## 1. Public now

The whole of this repo is AGPL-3.0-or-later. What it contains and why it is public:

| Area | What it is | Why public |
|---|---|---|
| `Shelfbound.Core` | Domain models + the versioned **snapshot contract** + serializers (pure, no I/O) | The interop seam every consumer talks through; must be open and language-neutral |
| `Shelfbound.Steam` | Local Steam scanner (VDF/ACF, modern + legacy categories, device specs) + Steam Web API client + client-side enrichment merge | The auditable differentiator — reads *your* machine; users must be able to see exactly what is read |
| `Shelfbound.Query` | Merged library view (facts + user-data) + deterministic filter/sort/summary + `ProfileQuery` + the **lean local recommendation cards** | Reusable, deterministic query logic over the snapshot; no moat value on its own |
| `Shelfbound.Storage` | Local config, the identity seam, and the local user-data store | Local-first persistence; the hosted layer swaps its own store behind the same seam |
| `Shelfbound.Client` | Shared scan-to-snapshot builder + server client | Client-side plumbing reused by the CLI and tray |
| `Shelfbound.Cli` / `Shelfbound.Mcp` / `Shelfbound.Tray` | The `shelfbound` CLI, the local MCP server, and the tray agent | The things a user actually runs locally |

**The lean local recommender is public on purpose — and is not the hosted engine.**
`Shelfbound.Query`'s recommendation cards are simple, deterministic heuristics over data the snapshot
already has (installed-but-unplayed, paused, recently-added, free-up-space), framed for the current
device. That is a genuinely useful local feature and stays free. It is a **different thing** from the
hosted scoring engine (item 1 under *Permanently private*), which is where the real ranking
intelligence lives. Keeping a capable local recommender public does not weaken the boundary — what
stays private is the hosted engine's ranking intelligence, not the existence of "recommendations".

## 2. Candidates to move public (recommendations, not moves)

Pieces that today live in the **private** product but are pure, snapshot/local-shaped, and carry no
business or scoring value — so publishing them would help contributors and any snapshot consumer without
giving anything away. **Each is a recommendation only; nothing is moved here.** Every accepted one is a
separate task (see *How a move happens* below).

A candidate should pass all four tests: **(a)** pure / deterministic, **(b)** operates on the public
snapshot or on local/device data, **(c)** no moat, monetization, or strategy value, **(d)** not coupled
to a private type.

- **A conservative title-normalization utility** — a small, pure helper that produces a stable,
  comparable form of a game title (case, trademark glyphs, punctuation, whitespace) for matching and
  cross-store dedup. It is not a fuzzy matcher; it is exactly the kind of generic string utility a
  snapshot consumer wants.
  - *Benefit:* useful to anyone building on the snapshot; a natural fit next to the models in
    `Shelfbound.Core` (or `Shelfbound.Query`).
  - *CLA/relicense:* trivially safe. It becomes AGPL; the hosted product keeps using it via the
    published package, and the [CLA](../../CLA.md) already lets the maintainer reuse any external
    contribution in the closed product. Clean, unblocked move.
- **An install-history derivation** — a pure, deterministic routine that reconstructs a per-game
  install/uninstall timeline by **diffing an owner's snapshot history**. It depends only on the public
  snapshot contract, and its one hard rule (never invent an uninstall from missing/partial snapshots) is
  a snapshot-correctness concern, not a business one.
  - *Benefit:* a local user keeping their own snapshots could derive their own timeline; it belongs
    conceptually with the public snapshot tooling.
  - *CLA/relicense:* safe. Publish **only the derivation algorithm** — the hosted product's *storage and
    retention* of snapshot history remains a paid convenience, and nothing about the algorithm reveals
    that. Becomes AGPL; CLA covers contributions. Unblocked move.
- **(Borderline — recommend keep private for now) device-fit evaluation.** A deterministic evaluator
  that grades "will this run *here*?" from a device's capability against a game's requirements is pure in
  isolation, and device-spec *collection* is already public (`Shelfbound.Steam`). But today it is coupled
  to a **private** game-requirements type produced by a server-side (paid) enrichment path, and
  device-aware fit is a headline hosted selling point. It is a candidate *only if* it were first
  decoupled from the private type **and** device-fit were decided not to be a paid differentiator —
  which it currently is. Recommendation: **keep private**, revisit only if both change.
- **Not worth moving:** tiny incidental helpers that happen to be pure (e.g. a money formatter) but live
  inside private business modules. Moving them for purity's sake is churn with no contributor benefit;
  leave them where they are used.

## 3. Permanently private (hard rule)

Each stays in the private product; one-line why. **Non-negotiable.**

- **The hosted recommendation/scoring engine** — the product's core judgement and differentiator; the
  ranking intelligence is the thing people would pay for.
- **Learned taste/affinity modelling** — the personal-taste model built from a user's opinions; the
  learned model is proprietary, while the commodity AI reasoning layered on top is not.
- **Cross-source enrichment fusion / verdicts** — the logic that reconciles many third-party sources
  into a single judgement; the aggregation *is* product value.
- **The enrichment provider fleet + server-side credentials and cost/rate posture** — third-party API
  keys, quotas, and the economics of running them are inherently private (see also
  [privacy-and-data.md](./privacy-and-data.md); user-supplied keys in the core are the user's own).
- **Server-side LLM usage** — metered, cost-bearing compute; deliberately not in the free local core.
- **The hosted taste/tracking store** — the server-side record of per-user taste/state the engine reads.
- **Accounts, auth, entitlements/plans, hosted persistence, and the hosted MCP implementation** — the
  hosted product itself: identity, monetization gating, and per-user server data.
- **Buy/discovery, deal, and affiliate logic** — monetization mechanics.
- **Product strategy, business model, and operations docs** — must never enter public history.

The public repo may *acknowledge* that a private hosted product exists (it already does, openly), but
never carry its internals, economics, or strategy.

## Leak check (is anything already on the wrong side?)

Swept this public repo for hosted/product, monetization, secrets, and engine/fusion detail. **Overall
clean** — [AGENTS.md](../../AGENTS.md) already encodes the "never put hosted/product/monetization/secrets
here" rule, and it is being followed. Specifics:

- **Finding (low): a public doc points into the private repo.** `mcp-design.md`'s "Planned" section
  links to a doc *path inside the private repo* by name. It leaks no secret, but a public reader cannot
  follow it and it needlessly names private structure. **Recommend:** describe the planned MCP tools
  without the private-repo pointer (drop or reword the parenthetical). *(Fixed 2026-07-02: the "Planned"
  parenthetical now describes the tracking/taste layer without naming the private repo or its doc path.)*
- **Not a leak (intended):** "the local Steam data is the moat" (in `PROJECT.md`) is public-safe framing
  — it states the open value proposition and reveals nothing about the private engine. Leave it.
- **Not a leak (intended):** the tray's `localhost` server/web URLs are dev defaults; production URLs are
  deliberately not committed yet (a known pre-public task in [PROJECT.md](./PROJECT.md)). No hosted
  endpoint is exposed.
- **No secrets found:** the only API-key handling in the core is the **user's own** Steam Web API key,
  which is never stored in the repo and never logged (see [DECISIONS.md](./DECISIONS.md) "Config").

**Clean-move blockers** (for the candidates above): the title-normalization utility and the
install-history derivation are cleanly movable (they depend only on `Shelfbound.Core`). Device-fit
evaluation is **blocked** from a clean move by its coupling to a private game-requirements type; it would
have to be decoupled first.

## How a move happens (when a candidate is accepted)

Not done here — recorded so an accepted recommendation is cheap to execute:

1. Extract the piece into the right public assembly (`Shelfbound.Core` for contract-shaped utilities,
   `Shelfbound.Query` for query/derivation), with tests.
2. Keep the hosted product consuming it via the **published package** — no forked copy. The
   [CLA](../../CLA.md) keeps external contributions reusable in the closed product.
3. Bump/publish the package and update the private consumer's reference.

Publishing is one-way (public history is forever), so each move is a small, deliberate PR — never a bulk
sweep.

## See also

- [DECISIONS.md](./DECISIONS.md) — "Two repositories", "License", and the OSS-boundary entry.
- [ARCHITECTURE.md](./ARCHITECTURE.md) — the repository boundary and the snapshot seam.
- [CLA.md](../../CLA.md) — the dual grant that makes public contributions reusable in the hosted product.
- [AGENTS.md](../../AGENTS.md) — the always-on "keep hosted/product/secrets out of this repo" rule.
