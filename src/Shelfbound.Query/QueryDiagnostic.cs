using System.Text.Json.Serialization;

namespace Shelfbound.Query;

/// <summary>The stable severity vocabulary for query diagnostics.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QueryDiagnosticSeverity>))]
public enum QueryDiagnosticSeverity
{
    [JsonStringEnumMemberName("error")]
    Error,
}

/// <summary>A zero-based UTF-16 source span, shared exactly by C# and browser JavaScript.</summary>
public sealed record QuerySourceSpan(int Start, int Length);

/// <summary>
/// A structured parser, validation, or capability failure. Raw input stays attached to the result for editor
/// recovery; consumers decide separately whether to reject the whole request or run the resolved remainder.
/// </summary>
public sealed record QueryDiagnostic
{
    public required string Code { get; init; }
    public QueryDiagnosticSeverity Severity { get; init; } = QueryDiagnosticSeverity.Error;
    public required string Field { get; init; }
    public string? Facet { get; init; }
    public QuerySourceSpan? Span { get; init; }
    public string? RawToken { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> Candidates { get; init; } = [];
    public string? ValidBareForm { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}

/// <summary>A canonical resolved query plus every diagnostic needed to repair the original text.</summary>
public sealed record QueryParseResult(QueryPlanQuery Query, IReadOnlyList<QueryDiagnostic> Diagnostics)
{
    [JsonIgnore]
    public bool IsValid => Diagnostics.Count == 0;
}
