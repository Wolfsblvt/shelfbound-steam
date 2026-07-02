# Shelfbound — MCP Design (local server)

How the open-source **local** MCP server exposes library context to AI clients. **Implemented**
(`Shelfbound.Mcp`, stdio) — read tools and write/remember tools both exist, backed by the
`Shelfbound.Storage` user-data store (statuses, ratings, completion, memories, category meanings).

Built on the official C# MCP SDK (`ModelContextProtocol`). The server scans the library on startup
(optionally enriched via the Steam Web API) and serves it; it can also load a snapshot file. Query
logic lives in the reusable `Shelfbound.Query` engine.

## Philosophy

- **Provide structured facts; let the AI reason.** Shelfbound's job is reliable, well-shaped data —
  not AI summaries the client can already produce.
- **Deterministic over LLM** wherever possible (filters, lookups).
- **Avoid tool bloat.** Too many narrow tools makes models pick worse. Prefer a small set:
  1. one powerful, parameterized `search_library`,
  2. a few semantic/deterministic shortcuts for very common asks,
  3. a few **write** tools for durable user context.
- **Writes are explicit and safe** — persist only what the user clearly states; everything is
  editable/deletable (see guardrails below and [privacy-and-data.md](./privacy-and-data.md)).

## Read tools (candidates)

`get_library_summary`, `search_library` (the workhorse), `get_game_details`, `get_categories`,
`get_installed_games`, `find_installed_unplayed_games`, `find_backlog_candidates`, `compare_games`,
`get_user_taste_profile`, `get_game_opinion`, `get_recommendation_context`.

**Built:** `get_recommendations` — deterministic "what to play/uninstall next?" cards (installed-but-unplayed,
framed for the Steam Deck only when the device is a Deck; paused; recently added; free-up-space) via the
shared `RecommendationEngine`. Reused by the local MCP server (and the hosted layer) through the shared
engine. Enrichment (completion times, ProtonDB) will add "short enough to finish" / "Gold on Deck" later.

## Write tools — MCP as a memory interface

MCP isn't only library search: it's the primary way to **maintain the user's gaming memory**. The server
instructions push the model to record useful facts whenever the user reveals them, then reason over them.

**Built:** `record_game_status`, `record_game_opinion` (rating + aspects), `set_game_completion`,
`set_category_definition`, `remember`, plus `delete_memory` / `get_game_user_data` / `get_remembered`.

**Planned (the tracking/taste layer — advanced taste modelling and per-game review queues):**
- `record_game_rating` (loved/liked/mixed/disliked/never_again) and `record_game_reasons` (default + custom
  **reason chips** — the "why", which matters more than the score).
- `record_played_elsewhere` / `record_external_ownership` (platform = Switch/PS5/Epic/GOG/Game Pass…) — so
  Shelfbound stops recommending the Steam version of a game already finished elsewhere.
- `set_category_rule` (richer than `set_category_definition`: meaning + action + confidence mode) and the
  **review-queue** tools `get_pending_review_items` / `confirm_inference` / `dismiss_inference` — confirm
  soft, category-inferred states into explicit ones.
- `record_recommendation_feedback` (dismiss with a reason → negative signal).

Tool descriptions must *encourage saving explicit durable facts* the user clearly states — and **never**
auto-promote an inference (e.g. a `Favorites` category) to an explicit rating without confirmation. Example
behaviors: "I finished Outer Wilds" → status finished; "dropped Hades, roguelikes stress me out" → dropped
+ negative chips `roguelike_fatigue, stressful` + remembered preference; "played Persona 5 on Switch" →
played_elsewhere + platform, drop from Steam backlog recs.

## Recency

Game results include recency as human phrases (`installedOrUpdatedAgo`, `lastPlayedAgo`, `addedAgo`)
alongside raw dates — models weight "3 days ago" over "2026-06-24". Steam exposes no purchase date, so
"added" is inferred from when Shelfbound first observed the game owned (relative to a baseline scan),
becoming meaningful as the user keeps using Shelfbound. Last-played for owned-but-not-installed games
comes from the Steam Web API (`rtime_last_played`).

## Steam deep links

Game results include `steam://` **deep links** so the model can offer one-click access on a machine with
Steam installed: `steam://run/<appid>` (**launch** — surface it clearly, it starts the game immediately),
`steam://store/<appid>` (store page), `steam://nav/games/details/<appid>` (library detail). The server
instructions tell the model to offer "launch / open in Steam" when it helps (e.g. after recommending what
to play next).

## Onboarding & "save it as you go"

`get_profile_status` reports whether the taste profile is set up and what to ask; the server ships
**instructions** (in the MCP `initialize` result) telling the model to save context when the user states
an opinion/status/meaning and to onboard when the profile is sparse. The deterministic profile logic lives
in the shared `ProfileQuery`; the model drives the conversation. `delete_memory` lets the user forget.

Onboarding is **never required, strongly recommended** — short, fast, skippable. The model can run it and
mark the profile **soft-onboarded** (the website may add more later; the two surfaces share that state).
Beyond onboarding, the profile keeps a persistent **"track these next" queue** (a few games to rate/track),
so collection stays opportunistic rather than a one-time chore.

### What we want onboarding to do (the target)

A short, **optional, run-once** flow — never an interrogation, never blocking the actual task:

1. The model calls `get_profile_status` at the start of any recommend/compare/choose-a-game task. If
   `isSetUp` is true, it **skips** onboarding (no nagging); if false, it offers a quick setup.
2. One sentence on what Shelfbound remembers and why.
3. Ask about a few **high-signal games** (`suggestedGamesToRate` = most-played, recognizable, not yet
   rated) — like it? what about it? → `record_game_opinion` (rating + aspects + the user's words as
   evidence).
4. Ask 1–2 **general taste** questions (genres/themes loved or avoided, typical time available) →
   `remember(scope=global)`. (Onboarding must capture global taste, not only per-game ratings.)
5. Ask what any `undefinedCategories` mean → `set_category_definition`.
6. Confirm what was saved and stop. The user can skip any step.

**Guardrails:** save only explicit statements; offer, don't force; keep it to a handful of questions.
**Determinism:** `get_profile_status` hands the model the concrete games/categories to ask about so it
doesn't invent them. `isSetUp` has a defined threshold (enough rated games or stated preferences).

### Improvements to make (next)

- Tighten the `initialize` instructions: onboard **once** when sparse, **skip** when set up, keep it short
  and skippable, and explicitly capture global taste.
- Consider a `get_onboarding_plan` tool returning the next concrete onboarding actions, so the flow is more
  reliable than free-form prose.
- Make `suggestedGamesToRate` favor high-playtime, recognizable, still-unrated games.

## Later / advanced

`find_games_by_mood`, `recommend_next_game`, `find_short_story_games`, `find_games_for_steam_deck`,
`find_dlc_gaps`, `find_games_by_natural_query`, `get_profile_gaps`. Some of these need external game
metadata, not just local data.

## Search/filter model

`search_library` operates on the **merged view** (snapshot facts + user-data) and accepts a rich,
composable, **deterministic** filter set:

- text query; app id/name
- categories/collections (include / exclude)
- installed yes/no; installed on a given device; Steam Deck status (later)
- played/unplayed; status (started/paused/finished/dropped)
- min/max playtime; max completion time (if external metadata is available)
- tags any/all/none; genre; mood; review score
- local-only/non-Steam; wishlist/owned
- hidden/ignored exclusions

Example asks it should satisfy: installed-but-unplayed; short backlog games; story-rich under 10h;
"in `Soon` but not installed"; cozy games available on Deck; owned-for-years-but-<2h-played; paused
games; games similar to loved ones.

## Memory / durable writes

Each stored fact carries: **type, scope, source, evidence, confidence, created_at, updated_at,
user-confirmed vs model-inferred, visibility/editability.**

Scopes: global user profile · game-specific · category-specific · device-specific · temporary/mood.

Where relevant, two destinations update together: the **global taste profile** and the **per-game
opinion/context**.

### Guardrails (critical)

Persist a durable fact **only when the user explicitly states it or asks to remember/mark/update**,
with evidence, and make it editable/deletable. Do **not** store weak guesses.

- ✅ "I loved Hades because short runs and progression felt great." → opinion: loved; aspects: short
  sessions, progression; source: conversation; confidence: high.
- ✅ "Mark Outer Wilds finished — played it on Game Pass." → status: finished; played_elsewhere: true.
- ✅ "My `Deck` category means portable games I want on Steam Deck." → category definition.
- ❌ User mentions Hades once → "user loves roguelikes."
- ❌ User asks about horror → "user likes horror."
- ❌ User says they *might* play something → stored as a favorite.

Model-inferred facts should be **proposed or low-confidence** until the user confirms. A future
"what Shelfbound remembers" view lets users review/edit/delete everything.

## LLM usage in MCP

Default to **deterministic**. Server-side LLM use is not part of the core MVP. If added later, it
must be cached and used only where it clearly beats a filter (e.g. natural-language → structured
filter translation, extracting taste signals from explicit notes). Never re-rank the whole library on
every call, regenerate unchanged summaries, or use an LLM where a deterministic filter suffices.
