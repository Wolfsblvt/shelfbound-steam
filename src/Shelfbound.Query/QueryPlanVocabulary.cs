namespace Shelfbound.Query;

/// <summary>The frozen names and normalization rules shared by every QueryPlan v1 producer.</summary>
public static class QueryPlanVocabulary
{
    public static IReadOnlyList<string> SessionValues { get; } =
        ["quickBreak", "shortSession", "standardSession", "longSession", "openEvening"];

    public static IReadOnlyList<string> FacetPrefixes { get; } =
    [
        "*", "tag", "genre", "chip", "category", "state", "rating", "owned", "installed",
        "playtime", "completion", "hoursToBeat", "rating100", "deckFit", "game",
    ];

    public static IReadOnlyList<string> FacetWireNames { get; } =
    [
        "any", "tag", "genre", "chip", "category", "state", "rating", "owned", "installed",
        "playtime", "completion", "hoursToBeat", "rating100", "deckFit", "game",
    ];

    public static IReadOnlyList<string> DirectiveNames { get; } = ["like", "session"];

    public static IReadOnlyList<string> TextPrefixes { get; } =
        [.. FacetPrefixes, .. DirectiveNames, "sort"];

    public static IReadOnlyList<string> SortFields { get; } =
        ["name", "playtime", "size", "lastPlayed", "completion", "hoursToBeat", "rating100", "added"];

    public static string FacetWireName(QueryFacet facet) => facet switch
    {
        QueryFacet.Any => "any",
        QueryFacet.Tag => "tag",
        QueryFacet.Genre => "genre",
        QueryFacet.Chip => "chip",
        QueryFacet.Category => "category",
        QueryFacet.State => "state",
        QueryFacet.Rating => "rating",
        QueryFacet.Owned => "owned",
        QueryFacet.Installed => "installed",
        QueryFacet.Playtime => "playtime",
        QueryFacet.Completion => "completion",
        QueryFacet.HoursToBeat => "hoursToBeat",
        QueryFacet.Rating100 => "rating100",
        QueryFacet.DeckFit => "deckFit",
        QueryFacet.Game => "game",
        _ => throw new ArgumentOutOfRangeException(nameof(facet), facet, null),
    };

    public static string FacetPrefix(QueryFacet facet) =>
        facet == QueryFacet.Any ? "*" : FacetWireName(facet);

    public static bool TryParseFacetPrefix(string value, out QueryFacet facet)
    {
        facet = value.ToLowerInvariant() switch
        {
            "*" => QueryFacet.Any,
            "tag" => QueryFacet.Tag,
            "genre" => QueryFacet.Genre,
            "chip" => QueryFacet.Chip,
            "category" => QueryFacet.Category,
            "state" => QueryFacet.State,
            "rating" => QueryFacet.Rating,
            "owned" => QueryFacet.Owned,
            "installed" => QueryFacet.Installed,
            "playtime" => QueryFacet.Playtime,
            "completion" => QueryFacet.Completion,
            "hourstobeat" => QueryFacet.HoursToBeat,
            "rating100" => QueryFacet.Rating100,
            "deckfit" => QueryFacet.DeckFit,
            "game" => QueryFacet.Game,
            _ => default,
        };
        return FacetPrefixes.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
    }

    public static string OperatorName(QueryGroupOperator op) => op switch
    {
        QueryGroupOperator.Match => "match",
        QueryGroupOperator.Exclude => "exclude",
        QueryGroupOperator.Prefer => "prefer",
        QueryGroupOperator.StrongPrefer => "strongPrefer",
        QueryGroupOperator.NiceToHave => "niceToHave",
        QueryGroupOperator.Avoid => "avoid",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    public static string OperatorSigil(QueryGroupOperator op) => op switch
    {
        QueryGroupOperator.Match => string.Empty,
        QueryGroupOperator.Exclude => "-",
        QueryGroupOperator.Prefer => "+",
        QueryGroupOperator.StrongPrefer => "++",
        QueryGroupOperator.NiceToHave => "?",
        QueryGroupOperator.Avoid => "?-",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    public static string SortFieldName(QuerySortField field) => field switch
    {
        QuerySortField.Name => "name",
        QuerySortField.Playtime => "playtime",
        QuerySortField.Size => "size",
        QuerySortField.LastPlayed => "lastPlayed",
        QuerySortField.Completion => "completion",
        QuerySortField.HoursToBeat => "hoursToBeat",
        QuerySortField.Rating100 => "rating100",
        QuerySortField.Added => "added",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    public static bool TryParseSortField(string value, out QuerySortField field)
    {
        field = value.ToLowerInvariant() switch
        {
            "name" => QuerySortField.Name,
            "playtime" => QuerySortField.Playtime,
            "size" => QuerySortField.Size,
            "lastplayed" => QuerySortField.LastPlayed,
            "completion" => QuerySortField.Completion,
            "hourstobeat" => QuerySortField.HoursToBeat,
            "rating100" => QuerySortField.Rating100,
            "added" => QuerySortField.Added,
            _ => default,
        };
        return SortFields.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The v1 shorthand defaults: alphabetical and duration sorts read low-to-high; magnitude, recency,
    /// completion, rating, and addition sorts surface the largest/newest value first.
    /// </summary>
    public static QuerySortDirection DefaultDirection(QuerySortField field) => field switch
    {
        QuerySortField.Name or QuerySortField.HoursToBeat => QuerySortDirection.Asc,
        _ => QuerySortDirection.Desc,
    };

    public static string DirectionName(QuerySortDirection direction) => direction switch
    {
        QuerySortDirection.Asc => "asc",
        QuerySortDirection.Desc => "desc",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    public static bool TryParseDirection(string value, out QuerySortDirection direction)
    {
        if (string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase))
        {
            direction = QuerySortDirection.Asc;
            return true;
        }
        if (string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase))
        {
            direction = QuerySortDirection.Desc;
            return true;
        }

        direction = default;
        return false;
    }

    public static bool TryNormalizeSession(string value, out string canonical)
    {
        canonical = SessionValues.FirstOrDefault(candidate =>
            string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return canonical.Length > 0;
    }

    public static string NormalizeTermValue(QueryFacet facet, string value)
    {
        string trimmed = value.Trim();
        return facet switch
        {
            QueryFacet.Owned or QueryFacet.Installed => NormalizeKnown(trimmed, ["yes", "no"]),
            QueryFacet.State => NormalizeKnown(trimmed,
                ["unplayed", "playing", "paused", "finished", "dropped", "satisfied", "everyNowAndThen"]),
            QueryFacet.Rating => NormalizeKnown(trimmed,
                ["unknown", "loved", "liked", "mixed", "disliked", "neverAgain", "skipped"]),
            QueryFacet.DeckFit => NormalizeKnown(trimmed, ["unsupported", "playable", "verified", "unknown"]),
            QueryFacet.Game => NormalizeGameReference(trimmed),
            _ => trimmed,
        };
    }

    public static string NormalizeLikeReference(string value)
    {
        string trimmed = value.Trim();
        int separator = trimmed.IndexOf(':');
        if (separator <= 0)
            return trimmed;

        string provider = trimmed[..separator];
        return provider.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? $"{provider.ToLowerInvariant()}:{trimmed[(separator + 1)..]}"
            : trimmed;
    }

    private static string NormalizeKnown(string value, IReadOnlyList<string> known) =>
        known.FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) ?? value;

    private static string NormalizeGameReference(string value)
    {
        if (value.Length > 0 && value.All(character => character is >= '0' and <= '9'))
        {
            string withoutLeadingZeroes = value.TrimStart('0');
            if (withoutLeadingZeroes.Length > 0)
                return $"steam:{withoutLeadingZeroes}";
        }
        return NormalizeLikeReference(value);
    }
}
