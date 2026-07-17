namespace Shelfbound.Query;

/// <summary>
/// Hand-rolled QueryPlan v1 text parser. It performs lexical/shape validation only: value resolution,
/// title matching, membership, and ranking belong to later consumers.
/// </summary>
public static class QueryTextParser
{
    public static QueryParseResult Parse(
        string? input,
        QueryCapabilityTarget? target = null,
        QueryParserLimits? limits = null)
    {
        string source = input ?? string.Empty;
        QueryParserLimits effectiveLimits = limits ?? new QueryParserLimits();
        var diagnostics = new List<QueryDiagnostic>();
        if (!ValidLimits(effectiveLimits))
        {
            diagnostics.Add(Diagnostic(
                "invalid_parser_limits",
                "limits",
                "Parser limits must all be positive integers.",
                data: new Dictionary<string, string>
                {
                    ["maxInputLength"] = effectiveLimits.MaxInputLength.ToString(),
                    ["maxTerms"] = effectiveLimits.MaxTerms.ToString(),
                    ["maxGroupTerms"] = effectiveLimits.MaxGroupTerms.ToString(),
                }));
            return new QueryParseResult(new QueryPlanQuery(), diagnostics);
        }

        if (source.Length > effectiveLimits.MaxInputLength)
        {
            diagnostics.Add(Diagnostic(
                "input_limit",
                "query",
                $"Query text must not exceed {effectiveLimits.MaxInputLength} UTF-16 code units.",
                span: new QuerySourceSpan(0, source.Length),
                data: new Dictionary<string, string>
                {
                    ["limit"] = effectiveLimits.MaxInputLength.ToString(),
                    ["actual"] = source.Length.ToString(),
                }));
            return new QueryParseResult(new QueryPlanQuery(), diagnostics);
        }

        IReadOnlyList<LexToken> tokens = Tokenize(source, diagnostics);
        var groups = new List<QueryGroup>();
        var groupSources = new List<GroupSource>();
        var likes = new List<string>();
        var seenLikes = new HashSet<string>(StringComparer.Ordinal);
        var textParts = new List<string>();
        QuerySort? sort = null;
        string? session = null;
        bool previousTokenWasText = false;
        bool exactText = false;
        int termCount = 0;

        foreach (LexToken token in tokens)
        {
            if (token.Malformed)
            {
                previousTokenWasText = false;
                continue;
            }

            IReadOnlyList<MemberToken> members = SplitMembers(token);
            if (members.Any(member => member.Raw.Length == 0))
            {
                diagnostics.Add(Diagnostic(
                    "empty_group_member",
                    "group",
                    "OR groups cannot contain an empty member.",
                    token.Span,
                    token.Raw));
                previousTokenWasText = false;
                continue;
            }
            if (members.Count > effectiveLimits.MaxGroupTerms)
            {
                diagnostics.Add(Diagnostic(
                    "group_term_limit",
                    "group",
                    $"A group cannot contain more than {effectiveLimits.MaxGroupTerms} members.",
                    token.Span,
                    token.Raw,
                    data: new Dictionary<string, string>
                    {
                        ["limit"] = effectiveLimits.MaxGroupTerms.ToString(),
                        ["actual"] = members.Count.ToString(),
                    }));
                previousTokenWasText = false;
                continue;
            }

            var parsedMembers = new List<ParsedMember>(members.Count);
            bool invalidMember = false;
            foreach (MemberToken member in members)
            {
                ParsedMember? parsed = ParseMember(member, diagnostics);
                if (parsed is null)
                {
                    invalidMember = true;
                    break;
                }
                parsedMembers.Add(parsed);
            }
            if (invalidMember)
            {
                previousTokenWasText = false;
                continue;
            }

            if (parsedMembers.Skip(1).Any(member => member.HasOperator))
            {
                diagnostics.Add(Diagnostic(
                    "mixed_group_operators",
                    "group.op",
                    "Operators apply to the whole group; only the first member may carry one.",
                    token.Span,
                    token.Raw));
                previousTokenWasText = false;
                continue;
            }

            ParsedMember first = parsedMembers[0];
            if (parsedMembers.Any(member => member.Kind == MemberKind.Text))
            {
                if (parsedMembers.Count != 1 || first.HasOperator)
                {
                    diagnostics.Add(Diagnostic(
                        first.HasOperator ? "operator_requires_facet" : "mixed_group_kinds",
                        "group",
                        first.HasOperator
                            ? "An ordinary operator requires an explicit facet prefix."
                            : "Free text cannot participate in an OR group.",
                        token.Span,
                        token.Raw));
                    previousTokenWasText = false;
                    continue;
                }

                if (textParts.Count > 0 && !previousTokenWasText)
                {
                    diagnostics.Add(Diagnostic(
                        "multiple_text_phrases",
                        "text",
                        "QueryPlan v1 allows one contiguous free-text phrase.",
                        token.Span,
                        token.Raw));
                    previousTokenWasText = false;
                    continue;
                }
                if (textParts.Count > 0 && (exactText || first.Quoted))
                {
                    diagnostics.Add(Diagnostic(
                        "mixed_text_exactness",
                        "text",
                        "An exact quoted title phrase cannot be combined with adjacent free-text tokens.",
                        token.Span,
                        token.Raw));
                    previousTokenWasText = false;
                    continue;
                }
                if (first.Value.Length == 0)
                {
                    diagnostics.Add(Diagnostic(
                        "empty_text",
                        "text",
                        "A free-text phrase cannot be empty.",
                        token.Span,
                        token.Raw));
                    previousTokenWasText = false;
                    continue;
                }
                if (textParts.Count == 0 && termCount + 1 > effectiveLimits.MaxTerms)
                {
                    diagnostics.Add(TermLimit(token, effectiveLimits.MaxTerms, termCount + 1));
                    previousTokenWasText = false;
                    continue;
                }

                textParts.Add(first.Value);
                exactText = first.Quoted;
                previousTokenWasText = true;
                continue;
            }

            previousTokenWasText = false;
            if (parsedMembers.Any(member => member.Kind == MemberKind.UnknownFacet))
                continue;

            MemberKind kind = first.Kind;
            if (parsedMembers.Any(member => member.Kind != kind))
            {
                diagnostics.Add(Diagnostic(
                    "mixed_group_kinds",
                    "group",
                    "Ordinary terms, directives, and sort clauses cannot share one OR group.",
                    token.Span,
                    token.Raw));
                continue;
            }

            if (kind is MemberKind.Like or MemberKind.Session or MemberKind.Sort && first.HasOperator)
            {
                string facet = kind switch
                {
                    MemberKind.Like => "like",
                    MemberKind.Session => "session",
                    _ => "sort",
                };
                string validBareForm = facet switch
                {
                    "like" => "like:<game>",
                    "session" => "session:<sessionValue>",
                    _ => "sort:<field>[:asc|desc]",
                };
                diagnostics.Add(Diagnostic(
                    "unsupported_operator_for_facet",
                    "group.op",
                    $"'{facet}' is valid only without an operator.",
                    token.Span,
                    token.Raw,
                    facet,
                    validBareForm: validBareForm,
                    data: new Dictionary<string, string>
                    {
                        ["operator"] = QueryPlanVocabulary.OperatorSigil(first.Operator),
                    }));
                continue;
            }

            switch (kind)
            {
                case MemberKind.Ordinary:
                    if (termCount + parsedMembers.Count + (textParts.Count > 0 ? 1 : 0) > effectiveLimits.MaxTerms)
                    {
                        diagnostics.Add(TermLimit(
                            token,
                            effectiveLimits.MaxTerms,
                            termCount + parsedMembers.Count + (textParts.Count > 0 ? 1 : 0)));
                        continue;
                    }

                    var terms = parsedMembers.Select(member => new QueryTerm
                    {
                        Facet = member.Facet!.Value,
                        Value = QueryPlanVocabulary.NormalizeTermValue(member.Facet.Value, member.Value),
                    }).ToList();
                    if (terms.Any(term => term.Value.Length == 0))
                    {
                        diagnostics.Add(Diagnostic(
                            "empty_value",
                            "term.value",
                            "Facet values must be non-empty.",
                            token.Span,
                            token.Raw,
                            QueryPlanVocabulary.FacetWireName(terms.First(term => term.Value.Length == 0).Facet)));
                        continue;
                    }

                    if (first.Operator == QueryGroupOperator.Exclude && terms.Count > 1)
                    {
                        for (int index = 0; index < terms.Count; index++)
                        {
                            groups.Add(new QueryGroup { Op = QueryGroupOperator.Exclude, Terms = [terms[index]] });
                            groupSources.Add(new GroupSource(parsedMembers[index].Source.Span, parsedMembers[index].Source.Raw));
                        }
                    }
                    else
                    {
                        groups.Add(new QueryGroup { Op = first.Operator, Terms = terms });
                        groupSources.Add(new GroupSource(token.Span, token.Raw));
                    }
                    termCount += terms.Count;
                    AddOrdinaryCapabilityDiagnostics(parsedMembers, first.Operator, token, target, diagnostics);
                    break;

                case MemberKind.Like:
                    if (termCount + parsedMembers.Count + (textParts.Count > 0 ? 1 : 0) > effectiveLimits.MaxTerms)
                    {
                        diagnostics.Add(TermLimit(
                            token,
                            effectiveLimits.MaxTerms,
                            termCount + parsedMembers.Count + (textParts.Count > 0 ? 1 : 0)));
                        continue;
                    }
                    foreach (ParsedMember member in parsedMembers)
                    {
                        if (member.Value.Length == 0)
                        {
                            diagnostics.Add(Diagnostic(
                                "empty_value",
                                "directives.like",
                                "A like directive requires a game reference.",
                                member.Source.Span,
                                member.Source.Raw,
                                "like"));
                            continue;
                        }
                        string value = QueryPlanVocabulary.NormalizeLikeReference(member.Value);
                        if (seenLikes.Add(value))
                            likes.Add(value);
                        AddCapabilityDiagnostic("like", member.Source, target, diagnostics);
                    }
                    termCount += parsedMembers.Count;
                    break;

                case MemberKind.Session:
                    if (parsedMembers.Count != 1)
                    {
                        diagnostics.Add(Diagnostic(
                            "directive_group_not_supported",
                            "directives.session",
                            "The session directive cannot participate in an OR group.",
                            token.Span,
                            token.Raw,
                            "session"));
                        continue;
                    }
                    if (termCount + 1 + (textParts.Count > 0 ? 1 : 0) > effectiveLimits.MaxTerms)
                    {
                        diagnostics.Add(TermLimit(token, effectiveLimits.MaxTerms, termCount + 1));
                        continue;
                    }
                    if (!QueryPlanVocabulary.TryNormalizeSession(first.Value, out string canonicalSession))
                    {
                        diagnostics.Add(Diagnostic(
                            "invalid_value",
                            "directives.session",
                            "The session directive value is not part of the QueryPlan v1 vocabulary.",
                            token.Span,
                            token.Raw,
                            "session",
                            candidates: Closest(first.Value, QueryPlanVocabulary.SessionValues)));
                        continue;
                    }
                    if (session is not null && !string.Equals(session, canonicalSession, StringComparison.Ordinal))
                    {
                        diagnostics.Add(Diagnostic(
                            "conflicting_session_directive",
                            "directives.session",
                            "QueryPlan v1 allows one session value.",
                            token.Span,
                            token.Raw,
                            "session",
                            data: new Dictionary<string, string> { ["existing"] = session }));
                        continue;
                    }
                    if (session is null)
                    {
                        session = canonicalSession;
                        termCount++;
                    }
                    AddCapabilityDiagnostic("session", first.Source, target, diagnostics);
                    break;

                case MemberKind.Sort:
                    if (parsedMembers.Count != 1)
                    {
                        diagnostics.Add(Diagnostic(
                            "sort_group_not_supported",
                            "sort",
                            "A sort clause cannot participate in an OR group.",
                            token.Span,
                            token.Raw,
                            "sort"));
                        continue;
                    }
                    if (sort is not null)
                    {
                        diagnostics.Add(Diagnostic(
                            "duplicate_sort",
                            "sort",
                            "QueryPlan v1 allows one deterministic sort.",
                            token.Span,
                            token.Raw,
                            "sort"));
                        continue;
                    }
                    QuerySort? parsedSort = ParseSort(first, diagnostics);
                    if (parsedSort is null)
                        continue;
                    if (termCount + 1 + (textParts.Count > 0 ? 1 : 0) > effectiveLimits.MaxTerms)
                    {
                        diagnostics.Add(TermLimit(token, effectiveLimits.MaxTerms, termCount + 1));
                        continue;
                    }
                    sort = parsedSort;
                    termCount++;
                    AddSortCapabilityDiagnostic(parsedSort.Field, first.Source, target, diagnostics);
                    break;
            }
        }

        AddContradictionDiagnostics(groups, groupSources, diagnostics);
        QueryText? text = textParts.Count == 0
            ? null
            : new QueryText { Phrase = string.Join(' ', textParts), Exact = exactText };
        return new QueryParseResult(
            new QueryPlanQuery
            {
                Groups = groups,
                Text = text,
                Sort = sort,
                Directives = new QueryDirectives { Like = likes, Session = session },
            },
            diagnostics);
    }

    private static bool ValidLimits(QueryParserLimits limits) =>
        limits.MaxInputLength > 0 && limits.MaxTerms > 0 && limits.MaxGroupTerms > 0;

    private static IReadOnlyList<LexToken> Tokenize(string input, List<QueryDiagnostic> diagnostics)
    {
        var tokens = new List<LexToken>();
        int index = 0;
        while (index < input.Length)
        {
            while (index < input.Length && IsWhitespace(input[index]))
                index++;
            if (index >= input.Length)
                break;

            int start = index;
            bool quoted = false;
            bool escaped = false;
            while (index < input.Length)
            {
                char character = input[index];
                if (escaped)
                {
                    escaped = false;
                    index++;
                    continue;
                }
                if (character == '\\')
                {
                    escaped = true;
                    index++;
                    continue;
                }
                if (character == '"')
                {
                    quoted = !quoted;
                    index++;
                    continue;
                }
                if (!quoted && IsWhitespace(character))
                    break;
                index++;
            }

            int length = index - start;
            string raw = input.Substring(start, length);
            bool malformed = false;
            if (escaped)
            {
                diagnostics.Add(Diagnostic(
                    "dangling_escape",
                    "query",
                    "A trailing backslash must escape another character.",
                    new QuerySourceSpan(start, length),
                    raw));
                malformed = true;
            }
            if (quoted)
            {
                diagnostics.Add(Diagnostic(
                    "unterminated_quote",
                    "query",
                    "Close the quoted value with a double quote.",
                    new QuerySourceSpan(start, length),
                    raw));
                malformed = true;
            }
            tokens.Add(new LexToken(raw, new QuerySourceSpan(start, length), malformed));
        }
        return tokens;
    }

    private static IReadOnlyList<MemberToken> SplitMembers(LexToken token)
    {
        var members = new List<MemberToken>();
        bool quoted = false;
        bool escaped = false;
        int start = 0;
        for (int index = 0; index < token.Raw.Length; index++)
        {
            char character = token.Raw[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (character == '\\')
            {
                escaped = true;
                continue;
            }
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && character == '|')
            {
                members.Add(Member(token, start, index - start));
                start = index + 1;
            }
        }
        members.Add(Member(token, start, token.Raw.Length - start));
        return members;
    }

    private static MemberToken Member(LexToken token, int offset, int length) => new(
        token.Raw.Substring(offset, length),
        new QuerySourceSpan(token.Span.Start + offset, length));

    private static ParsedMember? ParseMember(MemberToken member, List<QueryDiagnostic> diagnostics)
    {
        (QueryGroupOperator op, int operatorLength) = ReadOperator(member.Raw);
        string core = member.Raw[operatorLength..];
        if (core.Length == 0)
        {
            diagnostics.Add(Diagnostic(
                "missing_term",
                "term",
                "An operator must be followed by an explicit faceted term.",
                member.Span,
                member.Raw));
            return null;
        }

        int colon = FindUnescaped(core, ':');
        if (colon < 0)
        {
            if (!TryDecode(core, member, out string text, out bool quoted, diagnostics))
                return null;
            return new ParsedMember(MemberKind.Text, op, operatorLength > 0, null, text, quoted, member);
        }

        string facetText = core[..colon];
        if (facetText.Length == 0 || facetText.Any(character => character is '\\' or '"'))
        {
            diagnostics.Add(Diagnostic(
                "invalid_facet",
                "facet",
                "Facet names cannot be empty, quoted, or escaped.",
                member.Span,
                member.Raw));
            return null;
        }
        string valueText = core[(colon + 1)..];
        if (!TryDecode(valueText, member, out string value, out bool quotedValue, diagnostics))
            return null;

        string lowerFacet = facetText.ToLowerInvariant();
        MemberKind kind = lowerFacet switch
        {
            "like" => MemberKind.Like,
            "session" => MemberKind.Session,
            "sort" => MemberKind.Sort,
            _ => MemberKind.Ordinary,
        };
        if (kind != MemberKind.Ordinary)
            return new ParsedMember(kind, op, operatorLength > 0, null, value, quotedValue, member);

        if (!QueryPlanVocabulary.TryParseFacetPrefix(facetText, out QueryFacet facet))
        {
            diagnostics.Add(Diagnostic(
                "unknown_facet",
                "facet",
                "The facet name is not part of QueryPlan v1.",
                member.Span,
                member.Raw,
                facetText,
                candidates: Closest(facetText, QueryPlanVocabulary.TextPrefixes)));
            return new ParsedMember(MemberKind.UnknownFacet, op, operatorLength > 0, null, value, quotedValue, member);
        }
        if (value.StartsWith('!'))
        {
            diagnostics.Add(Diagnostic(
                "unsupported_value_operator",
                "term.value",
                "Negation uses the group-level '-' operator; value-level '!' is not part of QueryPlan v1.",
                member.Span,
                member.Raw,
                QueryPlanVocabulary.FacetWireName(facet),
                validBareForm: $"-{QueryPlanVocabulary.FacetPrefix(facet)}:{value[1..]}"));
            return null;
        }
        return new ParsedMember(MemberKind.Ordinary, op, operatorLength > 0, facet, value, quotedValue, member);
    }

    private static QuerySort? ParseSort(ParsedMember member, List<QueryDiagnostic> diagnostics)
    {
        string[] parts = member.Value.Split(':');
        if (parts.Length is < 1 or > 2 || parts[0].Length == 0)
        {
            diagnostics.Add(Diagnostic(
                "invalid_sort",
                "sort",
                "Use sort:<field>[:asc|desc].",
                member.Source.Span,
                member.Source.Raw,
                "sort",
                candidates: QueryPlanVocabulary.SortFields));
            return null;
        }
        if (!QueryPlanVocabulary.TryParseSortField(parts[0], out QuerySortField field))
        {
            diagnostics.Add(Diagnostic(
                "invalid_sort_field",
                "sort.field",
                "The sort field is not part of QueryPlan v1.",
                member.Source.Span,
                member.Source.Raw,
                "sort",
                candidates: Closest(parts[0], QueryPlanVocabulary.SortFields)));
            return null;
        }

        QuerySortDirection direction = QueryPlanVocabulary.DefaultDirection(field);
        if (parts.Length == 2 && !QueryPlanVocabulary.TryParseDirection(parts[1], out direction))
        {
            diagnostics.Add(Diagnostic(
                "invalid_sort_direction",
                "sort.direction",
                "Sort direction must be 'asc' or 'desc'.",
                member.Source.Span,
                member.Source.Raw,
                "sort",
                candidates: ["asc", "desc"]));
            return null;
        }
        return new QuerySort { Field = field, Direction = direction };
    }

    private static bool TryDecode(
        string raw,
        MemberToken source,
        out string value,
        out bool quoted,
        List<QueryDiagnostic> diagnostics)
    {
        value = string.Empty;
        quoted = false;
        var quotePositions = new List<int>();
        bool escaped = false;
        for (int index = 0; index < raw.Length; index++)
        {
            char character = raw[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (character == '\\')
            {
                escaped = true;
                continue;
            }
            if (character == '"')
                quotePositions.Add(index);
        }

        if (quotePositions.Count > 0)
        {
            bool surroundsValue = quotePositions.Count == 2 && quotePositions[0] == 0 && quotePositions[1] == raw.Length - 1;
            if (!surroundsValue)
            {
                diagnostics.Add(Diagnostic(
                    "invalid_quote_placement",
                    "query",
                    "Double quotes must surround the complete text or facet value.",
                    source.Span,
                    source.Raw));
                return false;
            }
            quoted = true;
            raw = raw[1..^1];
        }

        var decoded = new System.Text.StringBuilder(raw.Length);
        escaped = false;
        foreach (char character in raw)
        {
            if (escaped)
            {
                decoded.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else
            {
                decoded.Append(character);
            }
        }
        if (escaped)
        {
            diagnostics.Add(Diagnostic(
                "dangling_escape",
                "query",
                "A trailing backslash must escape another character.",
                source.Span,
                source.Raw));
            return false;
        }
        value = decoded.ToString();
        return true;
    }

    private static (QueryGroupOperator Operator, int Length) ReadOperator(string raw)
    {
        if (raw.StartsWith("++", StringComparison.Ordinal))
            return (QueryGroupOperator.StrongPrefer, 2);
        if (raw.StartsWith("?-", StringComparison.Ordinal))
            return (QueryGroupOperator.Avoid, 2);
        if (raw.StartsWith('+'))
            return (QueryGroupOperator.Prefer, 1);
        if (raw.StartsWith('?'))
            return (QueryGroupOperator.NiceToHave, 1);
        if (raw.StartsWith('-'))
            return (QueryGroupOperator.Exclude, 1);
        return (QueryGroupOperator.Match, 0);
    }

    private static int FindUnescaped(string value, char sought)
    {
        bool quoted = false;
        bool escaped = false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (character == '\\')
            {
                escaped = true;
                continue;
            }
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && character == sought)
                return index;
        }
        return -1;
    }

    private static void AddOrdinaryCapabilityDiagnostics(
        IReadOnlyList<ParsedMember> members,
        QueryGroupOperator op,
        LexToken token,
        QueryCapabilityTarget? target,
        List<QueryDiagnostic> diagnostics)
    {
        if (target is null)
            return;
        if (!QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Operator(op), target.Value))
        {
            diagnostics.Add(Diagnostic(
                "unsupported_operator",
                "group.op",
                $"The '{QueryPlanVocabulary.OperatorName(op)}' operator is not supported by the {TargetName(target.Value)} target.",
                token.Span,
                token.Raw,
                data: new Dictionary<string, string>
                {
                    ["operator"] = QueryPlanVocabulary.OperatorName(op),
                    ["target"] = TargetName(target.Value),
                }));
        }
        foreach (ParsedMember member in members)
        {
            QueryFacet facet = member.Facet!.Value;
            if (!QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Facet(facet), target.Value))
            {
                string name = QueryPlanVocabulary.FacetWireName(facet);
                diagnostics.Add(UnsupportedFacet(name, member.Source, target.Value));
            }
        }
    }

    private static void AddCapabilityDiagnostic(
        string directive,
        MemberToken source,
        QueryCapabilityTarget? target,
        List<QueryDiagnostic> diagnostics)
    {
        if (target is not null && !QueryPlanCapabilities.IsSupported(
            QueryPlanCapabilities.Directive(directive), target.Value))
        {
            diagnostics.Add(UnsupportedFacet(directive, source, target.Value));
        }
    }

    private static void AddSortCapabilityDiagnostic(
        QuerySortField field,
        MemberToken source,
        QueryCapabilityTarget? target,
        List<QueryDiagnostic> diagnostics)
    {
        if (target is not null && !QueryPlanCapabilities.IsSupported(QueryPlanCapabilities.Sort(field), target.Value))
            diagnostics.Add(UnsupportedFacet("sort", source, target.Value));
    }

    private static QueryDiagnostic UnsupportedFacet(string facet, MemberToken source, QueryCapabilityTarget target) =>
        Diagnostic(
            "unsupported_facet",
            "facet",
            $"The '{facet}' facet is not supported by the {TargetName(target)} target.",
            source.Span,
            source.Raw,
            facet,
            data: new Dictionary<string, string> { ["target"] = TargetName(target) });

    private static string TargetName(QueryCapabilityTarget target) => target switch
    {
        QueryCapabilityTarget.Local => "local",
        QueryCapabilityTarget.Hosted => "hosted",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static void AddContradictionDiagnostics(
        IReadOnlyList<QueryGroup> groups,
        IReadOnlyList<GroupSource> sources,
        List<QueryDiagnostic> diagnostics)
    {
        var positive = new Dictionary<string, GroupSource>(StringComparer.OrdinalIgnoreCase);
        var excluded = new Dictionary<string, GroupSource>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < groups.Count; index++)
        {
            QueryGroup group = groups[index];
            if (group.Terms.Count != 1 || group.Op is QueryGroupOperator.NiceToHave or QueryGroupOperator.Avoid)
                continue;

            QueryTerm term = group.Terms[0];
            string key = $"{QueryPlanVocabulary.FacetWireName(term.Facet)}\0{term.Value}";
            bool isExcluded = group.Op == QueryGroupOperator.Exclude;
            Dictionary<string, GroupSource> opposite = isExcluded ? positive : excluded;
            Dictionary<string, GroupSource> own = isExcluded ? excluded : positive;
            if (opposite.TryGetValue(key, out GroupSource? first))
            {
                GroupSource current = sources[index];
                diagnostics.Add(Diagnostic(
                    "contradictory_terms",
                    "groups",
                    "The same ordinary term cannot be both required and excluded.",
                    current.Span,
                    current.Raw,
                    QueryPlanVocabulary.FacetWireName(term.Facet),
                    data: new Dictionary<string, string>
                    {
                        ["conflictingStart"] = first.Span.Start.ToString(),
                        ["conflictingLength"] = first.Span.Length.ToString(),
                    }));
            }
            else
            {
                own.TryAdd(key, sources[index]);
            }
        }
    }

    private static QueryDiagnostic TermLimit(LexToken token, int limit, int actual) => Diagnostic(
        "term_limit",
        "query",
        $"QueryPlan v1 cannot contain more than {limit} terms.",
        token.Span,
        token.Raw,
        data: new Dictionary<string, string>
        {
            ["limit"] = limit.ToString(),
            ["actual"] = actual.ToString(),
        });

    private static IReadOnlyList<string> Closest(string value, IReadOnlyList<string> candidates)
    {
        var ranked = candidates
            .Select(candidate => new { Candidate = candidate, Distance = EditDistance(value, candidate) })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .ToList();
        if (ranked.Count == 0 || ranked[0].Distance > 3)
            return [];
        int bestDistance = ranked[0].Distance;
        return ranked
            .Where(item => item.Distance == bestDistance)
            .Take(3)
            .Select(item => item.Candidate)
            .ToList();
    }

    private static int EditDistance(string left, string right)
    {
        string a = left.ToLowerInvariant();
        string b = right.ToLowerInvariant();
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int index = 0; index <= b.Length; index++)
            previous[index] = index;

        for (int row = 1; row <= a.Length; row++)
        {
            current[0] = row;
            for (int column = 1; column <= b.Length; column++)
            {
                int cost = a[row - 1] == b[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    private static QueryDiagnostic Diagnostic(
        string code,
        string field,
        string message,
        QuerySourceSpan? span = null,
        string? rawToken = null,
        string? facet = null,
        IReadOnlyList<string>? candidates = null,
        string? validBareForm = null,
        IReadOnlyDictionary<string, string>? data = null) => new()
        {
            Code = code,
            Field = field,
            Facet = facet,
            Span = span,
            RawToken = rawToken,
            Message = message,
            Candidates = candidates ?? [],
            ValidBareForm = validBareForm,
            Data = data ?? new Dictionary<string, string>(),
        };

    private static bool IsWhitespace(char character) => character is ' ' or '\t' or '\r' or '\n';

    private enum MemberKind
    {
        Text,
        Ordinary,
        Like,
        Session,
        Sort,
        UnknownFacet,
    }

    private sealed record LexToken(string Raw, QuerySourceSpan Span, bool Malformed);
    private sealed record MemberToken(string Raw, QuerySourceSpan Span);
    private sealed record ParsedMember(
        MemberKind Kind,
        QueryGroupOperator Operator,
        bool HasOperator,
        QueryFacet? Facet,
        string Value,
        bool Quoted,
        MemberToken Source);
    private sealed record GroupSource(QuerySourceSpan Span, string Raw);
}
