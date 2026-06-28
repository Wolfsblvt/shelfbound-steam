# Shelfbound — MCP Design (local server)

How the open-source **local** MCP server exposes library context to AI clients. **Not yet built** —
this captures the intended design so it isn't re-derived. It is the next major piece after the local
scanner.

Built on the official C# MCP SDK (`ModelContextProtocol`). The local server reads the snapshot plus a
local user-data store (notes/statuses/profile), all on the user's machine.

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

## Write tools (candidates)

`set_category_definition`, `record_game_opinion`, `record_game_status`, `set_game_note`,
`record_user_preference`, plus deletes (`delete_game_note`, `delete_memory`). Tool descriptions should
*encourage the model to save explicit durable facts* when the user clearly states them.

## Later / advanced

`find_games_by_mood`, `recommend_next_game`, `find_short_story_games`, `find_games_for_steam_deck`,
`find_dlc_gaps`, `find_games_by_natural_query`, `get_profile_gaps`. Some of these need external game
metadata, not just local data.

## Search/filter model

`search_library` should accept a rich, composable, **deterministic** filter set:

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
