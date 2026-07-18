using System.Text;
using System.Text.Json.Serialization;

namespace Shelfbound.Query;

/// <summary>A canonical text serialization attempt with structured failures for non-canonical typed input.</summary>
public sealed record QuerySerializationResult(string? Text, IReadOnlyList<QueryDiagnostic> Diagnostics)
{
    [JsonIgnore]
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>Canonical QueryPlan v1 text serializer.</summary>
public static class QueryTextSerializer
{
    public static QuerySerializationResult Serialize(QueryPlanQuery query, QueryParserLimits? limits = null)
    {
        QueryParserLimits effectiveLimits = limits ?? new QueryParserLimits();
        var diagnostics = ValidateShape(query, effectiveLimits);
        if (diagnostics.Count > 0)
            return new QuerySerializationResult(null, diagnostics);

        string text;
        try
        {
            var tokens = new List<string>();
            if (query.Text is not null)
                tokens.Add(EncodeText(query.Text));

            foreach (QueryGroup group in query.Groups)
            {
                string members = string.Join('|', group.Terms.Select(EncodeTerm));
                tokens.Add(QueryPlanVocabulary.OperatorSigil(group.Op) + members);
            }
            tokens.AddRange(query.Directives.Like.Select(value => $"like:{EncodeValue(value)}"));
            if (query.Directives.Session is not null)
                tokens.Add($"session:{EncodeValue(query.Directives.Session)}");
            if (query.Sort is not null)
            {
                tokens.Add(
                    $"sort:{QueryPlanVocabulary.SortFieldName(query.Sort.Field)}:" +
                    QueryPlanVocabulary.DirectionName(query.Sort.Direction));
            }
            text = string.Join(' ', tokens);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return InvalidContractValue(error.ParamName ?? "query");
        }

        QueryParseResult parsed = QueryTextParser.Parse(text, limits: effectiveLimits);
        if (!parsed.IsValid)
            return new QuerySerializationResult(null, parsed.Diagnostics);

        try
        {
            if (!string.Equals(
                QueryPlanJson.SerializeQuery(query),
                QueryPlanJson.SerializeQuery(parsed.Query),
                StringComparison.Ordinal))
            {
                return new QuerySerializationResult(null,
                [
                    Diagnostic(
                        "non_canonical_query",
                        "query",
                        "Normalize values, defaults, and collection ordering before serializing QueryPlan text."),
                ]);
            }
        }
        catch (ArgumentException error)
        {
            return InvalidContractValue(error.ParamName ?? "query");
        }

        return new QuerySerializationResult(text, []);
    }

    private static List<QueryDiagnostic> ValidateShape(QueryPlanQuery query, QueryParserLimits limits)
    {
        var diagnostics = new List<QueryDiagnostic>();
        if (limits.MaxInputLength <= 0 || limits.MaxTerms <= 0 || limits.MaxGroupTerms <= 0)
        {
            diagnostics.Add(Diagnostic(
                "invalid_parser_limits",
                "limits",
                "Parser limits must all be positive integers."));
            return diagnostics;
        }

        int termCount = query.Groups.Sum(group => group.Terms.Count)
            + query.Directives.Like.Count
            + (query.Directives.Session is null ? 0 : 1)
            + (query.Sort is null ? 0 : 1)
            + (query.Text is null ? 0 : 1);
        if (termCount > limits.MaxTerms)
        {
            diagnostics.Add(Diagnostic(
                "term_limit",
                "query",
                $"QueryPlan v1 cannot contain more than {limits.MaxTerms} terms.",
                data: new Dictionary<string, string>
                {
                    ["limit"] = limits.MaxTerms.ToString(),
                    ["actual"] = termCount.ToString(),
                }));
        }

        for (int index = 0; index < query.Groups.Count; index++)
        {
            QueryGroup group = query.Groups[index];
            bool validOperator = Enum.IsDefined(group.Op);
            if (!validOperator)
            {
                diagnostics.Add(InvalidContractValueDiagnostic($"groups[{index}].op"));
            }
            if (group.Terms.Count == 0)
            {
                diagnostics.Add(Diagnostic(
                    "empty_group",
                    $"groups[{index}]",
                    "An ordinary group must contain at least one term."));
            }
            if (group.Terms.Count > limits.MaxGroupTerms)
            {
                diagnostics.Add(Diagnostic(
                    "group_term_limit",
                    $"groups[{index}]",
                    $"A group cannot contain more than {limits.MaxGroupTerms} members."));
            }
            if (validOperator && group.Op == QueryGroupOperator.Exclude && group.Terms.Count > 1)
            {
                diagnostics.Add(Diagnostic(
                    "non_canonical_exclude_group",
                    $"groups[{index}]",
                    "Excluded OR-groups must be normalized into singleton groups by De Morgan's law."));
            }
            for (int termIndex = 0; termIndex < group.Terms.Count; termIndex++)
            {
                QueryTerm term = group.Terms[termIndex];
                if (!Enum.IsDefined(term.Facet))
                {
                    diagnostics.Add(InvalidContractValueDiagnostic($"groups[{index}].terms[{termIndex}].facet"));
                    continue;
                }
                if (term.Value.Length == 0)
                {
                    diagnostics.Add(Diagnostic(
                        "empty_value",
                        $"groups[{index}].terms[{termIndex}].value",
                        "Facet values must be non-empty.",
                        facet: QueryPlanVocabulary.FacetWireName(term.Facet)));
                }
                else if (!string.Equals(
                    term.Value,
                    QueryPlanVocabulary.NormalizeTermValue(term.Facet, term.Value),
                    StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "non_canonical_value",
                        $"groups[{index}].terms[{termIndex}].value",
                        "The facet value is not in canonical wire form.",
                        facet: QueryPlanVocabulary.FacetWireName(term.Facet)));
                }
            }
        }

        if (query.Text is not null)
        {
            if (query.Text.Phrase.Length == 0)
            {
                diagnostics.Add(Diagnostic("empty_text", "text.phrase", "A free-text phrase cannot be empty."));
            }
            else if (!query.Text.Exact && !string.Equals(
                query.Text.Phrase,
                NormalizeWhitespace(query.Text.Phrase),
                StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "non_canonical_text",
                    "text.phrase",
                    "Non-exact free text must use trimmed single-space separators."));
            }
        }

        if (query.Sort is not null)
        {
            if (!Enum.IsDefined(query.Sort.Field))
                diagnostics.Add(InvalidContractValueDiagnostic("sort.field"));
            if (!Enum.IsDefined(query.Sort.Direction))
                diagnostics.Add(InvalidContractValueDiagnostic("sort.direction"));
        }

        var uniqueLikes = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < query.Directives.Like.Count; index++)
        {
            string value = query.Directives.Like[index];
            if (value.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "empty_value",
                    $"directives.like[{index}]",
                    "A like directive requires a game reference.",
                    facet: "like"));
            }
            else if (!string.Equals(value, QueryPlanVocabulary.NormalizeLikeReference(value), StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "non_canonical_value",
                    $"directives.like[{index}]",
                    "The like reference is not in canonical wire form.",
                    facet: "like"));
            }
            else if (!uniqueLikes.Add(value))
            {
                diagnostics.Add(Diagnostic(
                    "duplicate_value",
                    $"directives.like[{index}]",
                    "Repeated like references must be deduplicated in canonical form.",
                    facet: "like"));
            }
        }

        if (query.Directives.Session is not null &&
            (!QueryPlanVocabulary.TryNormalizeSession(query.Directives.Session, out string canonicalSession) ||
             !string.Equals(query.Directives.Session, canonicalSession, StringComparison.Ordinal)))
        {
            diagnostics.Add(Diagnostic(
                "invalid_value",
                "directives.session",
                "The session directive is not in canonical QueryPlan v1 wire form.",
                facet: "session",
                candidates: QueryPlanVocabulary.SessionValues));
        }
        return diagnostics;
    }

    private static QuerySerializationResult InvalidContractValue(string field) =>
        new(null, [InvalidContractValueDiagnostic(field)]);

    private static QueryDiagnostic InvalidContractValueDiagnostic(string field) =>
        Diagnostic(
            "invalid_contract_value",
            field,
            "The typed query contains a value outside the frozen QueryPlan v1 vocabulary.");

    private static string EncodeTerm(QueryTerm term) =>
        $"{QueryPlanVocabulary.FacetPrefix(term.Facet)}:{EncodeValue(term.Value)}";

    private static string EncodeValue(string value)
    {
        bool quote = value.Length == 0 || value.Any(character =>
            character is ' ' or '\t' or '\r' or '\n' or '|' or '"' or '\\');
        if (!quote)
            return value;
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string EncodeText(QueryText text)
    {
        if (text.Exact)
            return $"\"{text.Phrase.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

        var output = new StringBuilder(text.Phrase.Length);
        bool tokenStart = true;
        foreach (char character in text.Phrase)
        {
            if (character == ' ')
            {
                output.Append(character);
                tokenStart = true;
                continue;
            }
            if (character is '\\' or '"' or '|' or ':' || tokenStart && character is '+' or '?' or '-')
                output.Append('\\');
            output.Append(character);
            tokenStart = false;
        }
        return output.ToString();
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static QueryDiagnostic Diagnostic(
        string code,
        string field,
        string message,
        string? facet = null,
        IReadOnlyList<string>? candidates = null,
        IReadOnlyDictionary<string, string>? data = null) => new()
        {
            Code = code,
            Field = field,
            Facet = facet,
            Message = message,
            Candidates = candidates ?? [],
            Data = data ?? new Dictionary<string, string>(),
        };
}
