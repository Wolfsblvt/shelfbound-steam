namespace Shelfbound.Query;

/// <summary>Structured validation for a typed QueryPlan and a declared execution capability target.</summary>
public static class QueryPlanValidator
{
    public static IReadOnlyList<QueryDiagnostic> Validate(
        QueryPlan plan,
        QueryCapabilityTarget target,
        QueryParserLimits? limits = null)
    {
        var diagnostics = new List<QueryDiagnostic>();
        if (plan.Version != QueryPlan.CurrentVersion)
        {
            diagnostics.Add(Diagnostic(
                "unsupported_version",
                "version",
                $"QueryPlan version {QueryPlan.CurrentVersion} is required.",
                data: new Dictionary<string, string>
                {
                    ["supported"] = QueryPlan.CurrentVersion.ToString(),
                    ["actual"] = plan.Version.ToString(),
                }));
        }

        diagnostics.AddRange(QueryTextSerializer.Serialize(plan.Query, limits).Diagnostics);
        if (!Enum.IsDefined(target))
        {
            diagnostics.Add(Diagnostic(
                "invalid_contract_value",
                "target",
                "The capability target is outside the frozen QueryPlan v1 vocabulary."));
            return diagnostics;
        }
        for (int groupIndex = 0; groupIndex < plan.Query.Groups.Count; groupIndex++)
        {
            QueryGroup group = plan.Query.Groups[groupIndex];
            if (Enum.IsDefined(group.Op) &&
                !QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Operator(group.Op), target))
            {
                diagnostics.Add(Diagnostic(
                    "unsupported_operator",
                    $"query.groups[{groupIndex}].op",
                    $"The '{QueryPlanVocabulary.OperatorName(group.Op)}' operator is not supported by the {TargetName(target)} target.",
                    data: new Dictionary<string, string>
                    {
                        ["operator"] = QueryPlanVocabulary.OperatorName(group.Op),
                        ["target"] = TargetName(target),
                    }));
            }

            for (int termIndex = 0; termIndex < group.Terms.Count; termIndex++)
            {
                QueryTerm term = group.Terms[termIndex];
                if (Enum.IsDefined(term.Facet) &&
                    !QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Facet(term.Facet), target))
                {
                    string facet = QueryPlanVocabulary.FacetWireName(term.Facet);
                    diagnostics.Add(UnsupportedFacet(
                        facet,
                        $"query.groups[{groupIndex}].terms[{termIndex}].facet",
                        target));
                }
            }
        }

        for (int index = 0; index < plan.Query.Directives.Like.Count; index++)
        {
            if (!QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Directive("like"), target))
                diagnostics.Add(UnsupportedFacet("like", $"query.directives.like[{index}]", target));
        }
        if (plan.Query.Directives.Session is not null &&
            !QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Directive("session"), target))
        {
            diagnostics.Add(UnsupportedFacet("session", "query.directives.session", target));
        }
        if (plan.Query.Sort is not null && Enum.IsDefined(plan.Query.Sort.Field) &&
            !QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Sort(plan.Query.Sort.Field), target))
        {
            diagnostics.Add(UnsupportedFacet("sort", "query.sort.field", target));
        }
        return diagnostics;
    }

    private static QueryDiagnostic UnsupportedFacet(string facet, string field, QueryCapabilityTarget target) =>
        Diagnostic(
            "unsupported_facet",
            field,
            $"The '{facet}' facet is not supported by the {TargetName(target)} target.",
            facet,
            new Dictionary<string, string> { ["target"] = TargetName(target) });

    private static QueryDiagnostic Diagnostic(
        string code,
        string field,
        string message,
        string? facet = null,
        IReadOnlyDictionary<string, string>? data = null) => new()
        {
            Code = code,
            Field = field,
            Message = message,
            Facet = facet,
            Data = data ?? new Dictionary<string, string>(),
        };

    private static string TargetName(QueryCapabilityTarget target) => target switch
    {
        QueryCapabilityTarget.Local => "local",
        QueryCapabilityTarget.Hosted => "hosted",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };
}
