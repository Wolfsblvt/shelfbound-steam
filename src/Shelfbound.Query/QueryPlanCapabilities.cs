using System.Text.Json.Serialization;

namespace Shelfbound.Query;

/// <summary>The surface whose executable subset is being validated.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueryCapabilityTarget>))]
public enum QueryCapabilityTarget
{
    [JsonStringEnumMemberName("local")]
    Local,

    [JsonStringEnumMemberName("hosted")]
    Hosted,
}

/// <summary>Where a frozen query feature can be resolved and executed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueryCapabilityAvailability>))]
public enum QueryCapabilityAvailability
{
    [JsonStringEnumMemberName("local")]
    Local,

    [JsonStringEnumMemberName("hostedOnly")]
    HostedOnly,

    [JsonStringEnumMemberName("resolutionDependent")]
    ResolutionDependent,
}

/// <summary>
/// Explicit v1 capability classification. Parsing is universal; this table decides whether a target may execute
/// the parsed feature. The wildcard is resolution-dependent because a local union may bind only local vocabulary.
/// </summary>
public static class QueryPlanCapabilities
{
    public static QueryCapabilityAvailability Facet(QueryFacet facet) => facet switch
    {
        QueryFacet.Category or QueryFacet.State or QueryFacet.Rating or QueryFacet.Owned or
            QueryFacet.Installed or QueryFacet.Playtime or QueryFacet.Completion => QueryCapabilityAvailability.Local,
        QueryFacet.Any => QueryCapabilityAvailability.ResolutionDependent,
        _ => QueryCapabilityAvailability.HostedOnly,
    };

    public static QueryCapabilityAvailability Sort(QuerySortField field) => field switch
    {
        QuerySortField.Name or QuerySortField.Playtime or QuerySortField.Size or QuerySortField.LastPlayed or
            QuerySortField.Completion or QuerySortField.Added => QueryCapabilityAvailability.Local,
        _ => QueryCapabilityAvailability.HostedOnly,
    };

    public static QueryCapabilityAvailability Directive(string directive) => directive switch
    {
        "like" or "session" => QueryCapabilityAvailability.HostedOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(directive), directive, null),
    };

    public static QueryCapabilityAvailability Operator(QueryGroupOperator op) => op switch
    {
        QueryGroupOperator.Match or QueryGroupOperator.Exclude => QueryCapabilityAvailability.Local,
        _ => QueryCapabilityAvailability.HostedOnly,
    };

    public static bool IsSupported(QueryCapabilityAvailability availability, QueryCapabilityTarget target) =>
        target == QueryCapabilityTarget.Hosted || availability != QueryCapabilityAvailability.HostedOnly;
}

/// <summary>Configurable hard bounds for the hand-rolled parser.</summary>
public sealed record QueryParserLimits
{
    public const int DefaultMaxInputLength = 512;
    public const int DefaultMaxTerms = 100;
    public const int DefaultMaxGroupTerms = 50;

    public int MaxInputLength { get; init; } = DefaultMaxInputLength;
    public int MaxTerms { get; init; } = DefaultMaxTerms;
    public int MaxGroupTerms { get; init; } = DefaultMaxGroupTerms;
}
