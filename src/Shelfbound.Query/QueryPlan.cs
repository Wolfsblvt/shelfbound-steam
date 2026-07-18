using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelfbound.Query;

/// <summary>The frozen ordinary-term facets understood by QueryPlan v1.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueryFacet>))]
public enum QueryFacet
{
    [JsonStringEnumMemberName("any")]
    Any,

    [JsonStringEnumMemberName("tag")]
    Tag,

    [JsonStringEnumMemberName("genre")]
    Genre,

    [JsonStringEnumMemberName("chip")]
    Chip,

    [JsonStringEnumMemberName("category")]
    Category,

    [JsonStringEnumMemberName("state")]
    State,

    [JsonStringEnumMemberName("rating")]
    Rating,

    [JsonStringEnumMemberName("owned")]
    Owned,

    [JsonStringEnumMemberName("installed")]
    Installed,

    [JsonStringEnumMemberName("playtime")]
    Playtime,

    [JsonStringEnumMemberName("completion")]
    Completion,

    [JsonStringEnumMemberName("hoursToBeat")]
    HoursToBeat,

    [JsonStringEnumMemberName("rating100")]
    Rating100,

    [JsonStringEnumMemberName("deckFit")]
    DeckFit,

    [JsonStringEnumMemberName("game")]
    Game,
}

/// <summary>
/// The v1 operator on an ordinary OR-group. Operators never attach to individual members.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueryGroupOperator>))]
public enum QueryGroupOperator
{
    [JsonStringEnumMemberName("match")]
    Match,

    [JsonStringEnumMemberName("exclude")]
    Exclude,

    [JsonStringEnumMemberName("prefer")]
    Prefer,

    [JsonStringEnumMemberName("strongPrefer")]
    StrongPrefer,

    [JsonStringEnumMemberName("niceToHave")]
    NiceToHave,

    [JsonStringEnumMemberName("avoid")]
    Avoid,
}

/// <summary>The deterministic sort fields frozen by QueryPlan v1.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuerySortField>))]
public enum QuerySortField
{
    [JsonStringEnumMemberName("name")]
    Name,

    [JsonStringEnumMemberName("playtime")]
    Playtime,

    [JsonStringEnumMemberName("size")]
    Size,

    [JsonStringEnumMemberName("lastPlayed")]
    LastPlayed,

    [JsonStringEnumMemberName("completion")]
    Completion,

    [JsonStringEnumMemberName("hoursToBeat")]
    HoursToBeat,

    [JsonStringEnumMemberName("rating100")]
    Rating100,

    [JsonStringEnumMemberName("added")]
    Added,
}

/// <summary>A canonical sort direction.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuerySortDirection>))]
public enum QuerySortDirection
{
    [JsonStringEnumMemberName("asc")]
    Asc,

    [JsonStringEnumMemberName("desc")]
    Desc,
}

/// <summary>A versioned QueryPlan request without viewer or transport context.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryPlan
{
    public const int CurrentVersion = 1;

    [JsonPropertyOrder(0)]
    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public QueryPlanQuery Query { get; init; } = new();
}

/// <summary>
/// The canonical text-round-trip object. Defaults are materialized so every producer emits the same shape.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryPlanQuery
{
    [JsonPropertyOrder(0)]
    [JsonRequired]
    public IReadOnlyList<QueryGroup> Groups { get; init; } = [];

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public QueryText? Text { get; init; }

    [JsonPropertyOrder(2)]
    [JsonRequired]
    public QuerySort? Sort { get; init; }

    [JsonPropertyOrder(3)]
    [JsonRequired]
    public QueryDirectives Directives { get; init; } = new();
}

/// <summary>One OR-group in the plan's top-level AND.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryGroup
{
    [JsonPropertyOrder(0)]
    [JsonRequired]
    public QueryGroupOperator Op { get; init; }

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public IReadOnlyList<QueryTerm> Terms { get; init; } = [];
}

/// <summary>One typed member of an ordinary query group.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryTerm
{
    [JsonPropertyOrder(0)]
    [JsonRequired]
    public QueryFacet Facet { get; init; }

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public string Value { get; init; } = string.Empty;
}

/// <summary>The single free-text title phrase lane.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryText
{
    [JsonPropertyOrder(0)]
    [JsonRequired]
    public string Phrase { get; init; } = string.Empty;

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public bool Exact { get; init; }
}

/// <summary>An explicit deterministic sort, including its materialized direction.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuerySort
{
    [JsonPropertyOrder(0)]
    [JsonRequired]
    public QuerySortField Field { get; init; }

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public QuerySortDirection Direction { get; init; }
}

/// <summary>Bare-only specialized directives, separate from ordinary groups.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QueryDirectives
{
    [JsonPropertyOrder(0)]
    [JsonRequired]
    public IReadOnlyList<string> Like { get; init; } = [];

    [JsonPropertyOrder(1)]
    [JsonRequired]
    public string? Session { get; init; }
}

/// <summary>Canonical System.Text.Json serialization for the versioned public contract.</summary>
public static class QueryPlanJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(QueryPlan plan) => JsonSerializer.Serialize(plan, Options);

    public static string SerializeQuery(QueryPlanQuery query) => JsonSerializer.Serialize(query, Options);
}
