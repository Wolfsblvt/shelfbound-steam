# QueryPlan v1 contract

`Shelfbound.Query` exposes a versioned, deterministic query contract and text grammar for consumers that
need to exchange search intent without inventing their own syntax. This is currently a **contract and parser
seam only**: `LibraryQueryEngine` and the local MCP `search_library` tool still execute the existing additive
`LibraryFilter` API. QueryPlan execution, title resolution, ranking, and UI behavior belong to later work.

## Public shape

`QueryPlan` contains only the version and the text-round-trip query object:

```json
{
  "version": 1,
  "query": {
    "groups": [],
    "text": null,
    "sort": null,
    "directives": { "like": [], "session": null }
  }
}
```

Viewer and transport context such as a ranking lens, offset, and limit are deliberately outside this public
object. Defaults are materialized and enum values use their frozen wire casing.

The query is a one-level AND of ordinary OR-groups. Every group owns one operator and one or more
`{ facet, value }` terms. A separate text lane carries at most one contiguous title phrase; a separate
directive channel carries `like` references and one `session` value.

## Text grammar

| Form | Meaning |
|---|---|
| `facet:value` | ordinary match group |
| `facet:a\|facet:b` | ordinary OR-group; the whole group is one top-level AND term |
| `-`, `+`, `++`, `?`, `?-` | exclude, prefer, strong-prefer, nice-to-have, or avoid the whole group |
| `like:<game>` | bare-only game-similarity directive; repeatable and deduplicated |
| `session:<value>` | bare-only session directive; repeated equal values collapse, conflicts fail |
| `sort:<field>[:asc\|desc]` | one deterministic sort with its direction materialized |
| unprefixed text | one contiguous free-text title phrase |
| `"quoted value"` | exact text, or a delimited facet/directive value |

Backslash escapes the next character. Operators apply only to the first member of an ordinary group;
per-member mixtures are invalid. A negative OR-group canonicalizes by De Morgan into ordered singleton
exclude groups. Negation exists only in group position (`-state:finished`); value-level `state:!finished`
is invalid. Quotes delimit or mark exactness—they do not create a hidden game facet.

`like` and `session` are not ordinary facets. Decorated forms such as `-like:...` or `?session:...` remain
invalid and produce `unsupported_operator_for_facet` with `facet`, the source token/span, and
`validBareForm`; they never degrade into a bare directive.

Canonical serialization orders free text, ordinary groups, repeated `like` values, `session`, then `sort`.
It preserves group/member order, emits explicit sort directions, applies value normalization, and quotes only
when required. The law is:

```text
parse(canonicalSerialize(query)).query == query
```

The shorthand sort defaults are `asc` for `name` and `hoursToBeat`, and `desc` for the other v1 sort fields.
The default defensive bounds are 512 UTF-16 code units, 100 total terms, and 50 members in one group; callers
may provide another positive `QueryParserLimits` instance.

## Vocabulary and capabilities

Frozen plan wire facets (the `any` wire value uses `*:` in text):

```text
any tag genre chip category state rating owned installed playtime completion
hoursToBeat rating100 deckFit game
```

Frozen sort fields:

```text
name playtime size lastPlayed completion hoursToBeat rating100 added
```

Frozen session values:

```text
quickBreak shortSession standardSession longSession openEvening
```

Facet names, operator/directive names, sort fields, and the listed enum-like values are wire contract.
Tag, chip, category, title, and provider-reference values remain data.

Parsing recognizes the whole grammar independently of execution capability. `QueryPlanCapabilities` classifies
features as `local`, `hostedOnly`, or `resolutionDependent`; parsing with a local target therefore reports
`unsupported_facet`/`unsupported_operator` for known hosted-only syntax instead of misreporting it as unknown.
Wildcard `*` is resolution-dependent because a consumer may bind a different set of value namespaces.

## Diagnostics and API

- `QueryTextParser.Parse(...)` returns the resolved canonical subset plus structured diagnostics.
- `QueryTextSerializer.Serialize(...)` rejects non-canonical typed input with structured diagnostics.
- `QueryPlanValidator.Validate(...)` validates a typed plan version, canonical shape, and target capabilities.
- `QueryPlanJson` writes canonical JSON for the full plan or its query object.

Diagnostics have a stable code and field plus optional facet, zero-based UTF-16 span, raw token, candidates,
bare-form repair, and machine-readable data. Callers choose policy: a strict server can reject any diagnostic;
an editor can preserve the raw invalid token while showing the parsed remainder. The parser does not resolve
titles or vocabulary values, read user/provider data, test membership, or calculate ranking.

## Conformance corpus

[`contracts/power-search/query-plan-v1.corpus.json`](../../contracts/power-search/query-plan-v1.corpus.json)
is the public canonical corpus. It covers G1–G25 with executable parse, canonical text, diagnostic, and local
capability expectations; future membership/ranking/title/recovery metadata is deliberately not asserted by this
slice. The C# suite consumes that file directly, and it is packed into `Shelfbound.Query` under the same path.
Any downstream language mirror must be byte-compared with this source before its own parser suite runs.
