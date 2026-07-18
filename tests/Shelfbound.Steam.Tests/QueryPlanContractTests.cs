using System.Text.Json;
using Shelfbound.Query;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class QueryPlanContractTests
{
    [Fact]
    public void Json_contract_materializes_defaults_and_frozen_wire_names()
    {
        var plan = new QueryPlan
        {
            Query = new QueryPlanQuery
            {
                Groups =
                [
                    new QueryGroup
                    {
                        Op = QueryGroupOperator.StrongPrefer,
                        Terms = [new QueryTerm { Facet = QueryFacet.Any, Value = "story-rich" }],
                    },
                ],
            },
        };

        QueryPlanJson.Serialize(plan).ShouldBe(
            "{\"version\":1,\"query\":{\"groups\":[{\"op\":\"strongPrefer\",\"terms\":[{\"facet\":\"any\",\"value\":\"story-rich\"}]}],\"text\":null,\"sort\":null,\"directives\":{\"like\":[],\"session\":null}}}");
    }

    [Fact]
    public void Wildcard_text_prefix_maps_to_any_wire_facet_without_accepting_any_as_text_syntax()
    {
        QueryTextParser.Parse("*:cozy").Query.Groups.ShouldHaveSingleItem()
            .Terms.ShouldHaveSingleItem().Facet.ShouldBe(QueryFacet.Any);
        QueryTextParser.Parse("any:cozy").Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("unknown_facet");
    }

    [Fact]
    public void Prefix_typos_include_directives_and_value_level_negation_is_rejected()
    {
        QueryTextParser.Parse("sesion:shortSession").Diagnostics.ShouldHaveSingleItem()
            .Candidates.ShouldContain("session");

        QueryDiagnostic negation = QueryTextParser.Parse("state:!finished").Diagnostics.ShouldHaveSingleItem();
        negation.Code.ShouldBe("unsupported_value_operator");
        negation.Facet.ShouldBe("state");
        negation.ValidBareForm.ShouldBe("-state:finished");
    }

    [Fact]
    public void Parser_normalizes_negative_or_by_de_morgan_and_materializes_sort_defaults()
    {
        QueryParseResult result = QueryTextParser.Parse("-tag:horror|genre:slasher sort:hoursToBeat");

        result.IsValid.ShouldBeTrue();
        result.Query.Groups.Count.ShouldBe(2);
        result.Query.Groups.ShouldAllBe(group => group.Op == QueryGroupOperator.Exclude && group.Terms.Count == 1);
        result.Query.Sort.ShouldBe(new QuerySort
        {
            Field = QuerySortField.HoursToBeat,
            Direction = QuerySortDirection.Asc,
        });
        QueryTextSerializer.Serialize(result.Query).Text.ShouldBe(
            "-tag:horror -genre:slasher sort:hoursToBeat:asc");
    }

    [Fact]
    public void Decorated_directives_remain_invalid_and_never_enter_the_plan()
    {
        string[] operators = ["-", "+", "++", "?", "?-"];
        foreach (string directive in new[] { "like:steam:292030", "session:shortSession" })
        {
            foreach (string op in operators)
            {
                QueryParseResult result = QueryTextParser.Parse(op + directive);

                result.IsValid.ShouldBeFalse();
                QueryDiagnostic diagnostic = result.Diagnostics.ShouldHaveSingleItem();
                diagnostic.Code.ShouldBe("unsupported_operator_for_facet");
                diagnostic.RawToken.ShouldBe(op + directive);
                diagnostic.Facet.ShouldBe(directive.StartsWith("like", StringComparison.Ordinal) ? "like" : "session");
                result.Query.Groups.ShouldBeEmpty();
                result.Query.Directives.Like.ShouldBeEmpty();
                result.Query.Directives.Session.ShouldBeNull();
            }
        }
    }

    [Fact]
    public void Canonical_text_round_trips_quotes_escapes_and_game_shorthand()
    {
        QueryParseResult result = QueryTextParser.Parse(
            "NieR\\:Automata tag:\"open world\" game:000620 like:\"The Witcher 3\"");

        result.IsValid.ShouldBeTrue();
        result.Query.Text.ShouldBe(new QueryText { Phrase = "NieR:Automata", Exact = false });
        result.Query.Groups[1].Terms.ShouldHaveSingleItem().Value.ShouldBe("steam:620");
        QuerySerializationResult serialized = QueryTextSerializer.Serialize(result.Query);
        serialized.IsValid.ShouldBeTrue();
        serialized.Text.ShouldBe(
            "NieR\\:Automata tag:\"open world\" game:steam:620 like:\"The Witcher 3\"");

        QueryParseResult reparsed = QueryTextParser.Parse(serialized.Text);
        QueryPlanJson.SerializeQuery(reparsed.Query).ShouldBe(QueryPlanJson.SerializeQuery(result.Query));
    }

    [Fact]
    public void Local_capability_target_reports_hosted_facets_directives_sorts_and_ranking_operators()
    {
        QueryParseResult result = QueryTextParser.Parse(
            "?installed:yes tag:cozy like:steam:292030 sort:rating100",
            QueryCapabilityTarget.Local);

        result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
            ["unsupported_operator", "unsupported_facet", "unsupported_facet", "unsupported_facet"]);
        result.Diagnostics.Where(diagnostic => diagnostic.Code == "unsupported_facet")
            .Select(diagnostic => diagnostic.Facet)
            .ShouldBe(["tag", "like", "sort"]);
        result.Query.Groups.Count.ShouldBe(2);
        result.Query.Directives.Like.ShouldBe(["steam:292030"]);
    }

    [Fact]
    public void Typed_plan_validation_is_structured_and_capability_aware()
    {
        var plan = new QueryPlan
        {
            Version = 2,
            Query = new QueryPlanQuery
            {
                Groups =
                [
                    new QueryGroup
                    {
                        Op = QueryGroupOperator.NiceToHave,
                        Terms = [new QueryTerm { Facet = QueryFacet.Tag, Value = "cozy" }],
                    },
                ],
                Directives = new QueryDirectives { Session = "shortSession" },
            },
        };

        QueryPlanValidator.Validate(plan, QueryCapabilityTarget.Local)
            .Select(diagnostic => diagnostic.Code)
            .ShouldBe(["unsupported_version", "unsupported_operator", "unsupported_facet", "unsupported_facet"]);
        QueryPlanValidator.Validate(plan with { Version = 1 }, QueryCapabilityTarget.Hosted).ShouldBeEmpty();
    }

    [Fact]
    public void Typed_contract_values_fail_structurally_without_throwing()
    {
        var query = new QueryPlanQuery
        {
            Groups =
            [
                new QueryGroup
                {
                    Op = (QueryGroupOperator)999,
                    Terms = [new QueryTerm { Facet = (QueryFacet)999, Value = "cozy" }],
                },
            ],
            Sort = new QuerySort
            {
                Field = (QuerySortField)999,
                Direction = (QuerySortDirection)999,
            },
        };

        QueryTextSerializer.Serialize(query).Diagnostics
            .Select(diagnostic => (diagnostic.Code, diagnostic.Field))
            .ShouldBe(
            [
                ("invalid_contract_value", "groups[0].op"),
                ("invalid_contract_value", "groups[0].terms[0].facet"),
                ("invalid_contract_value", "sort.field"),
                ("invalid_contract_value", "sort.direction"),
            ]);
    }

    [Fact]
    public void Repeated_like_values_deduplicate_and_conflicting_sessions_fail()
    {
        QueryParseResult result = QueryTextParser.Parse(
            "like:steam:292030|like:steam:292030 session:shortSession session:openEvening");

        result.Query.Directives.Like.ShouldBe(["steam:292030"]);
        result.Query.Directives.Session.ShouldBe("shortSession");
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("conflicting_session_directive");
    }

    [Fact]
    public void Bounds_return_structured_diagnostics()
    {
        QueryTextParser.Parse(new string('x', 513)).Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("input_limit");
        QueryTextParser.Parse("tag:a tag:b", limits: new QueryParserLimits { MaxTerms = 1 })
            .Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("term_limit");
        QueryTextParser.Parse("tag:a|tag:b", limits: new QueryParserLimits { MaxGroupTerms = 1 })
            .Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("group_term_limit");
    }

    [Fact]
    public void Canonical_round_trip_is_deterministic_over_generated_valid_queries()
    {
        var random = new Random(0x51E1F);
        QueryFacet[] facets = [QueryFacet.Tag, QueryFacet.Category];
        QueryGroupOperator[] operators = Enum.GetValues<QueryGroupOperator>();

        for (int iteration = 0; iteration < 250; iteration++)
        {
            int groupCount = random.Next(0, 8);
            var groups = new List<QueryGroup>();
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                QueryGroupOperator op = operators[random.Next(operators.Length)];
                int memberCount = op == QueryGroupOperator.Exclude ? 1 : random.Next(1, 4);
                var terms = new List<QueryTerm>();
                for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
                {
                    QueryFacet facet = facets[random.Next(facets.Length)];
                    string value = $"value-{iteration}-{groupIndex}-{memberIndex}";
                    terms.Add(new QueryTerm { Facet = facet, Value = value });
                }
                groups.Add(new QueryGroup { Op = op, Terms = terms });
            }

            var query = new QueryPlanQuery
            {
                Groups = groups,
                Text = iteration % 3 == 0
                    ? new QueryText { Phrase = iteration % 2 == 0 ? "Portal 2" : "ori", Exact = iteration % 2 != 0 }
                    : null,
                Sort = iteration % 5 == 0
                    ? new QuerySort { Field = QuerySortField.LastPlayed, Direction = QuerySortDirection.Desc }
                    : null,
                Directives = new QueryDirectives
                {
                    Like = iteration % 7 == 0 ? [$"steam:{iteration + 1}"] : [],
                    Session = iteration % 11 == 0 ? "shortSession" : null,
                },
            };

            QuerySerializationResult first = QueryTextSerializer.Serialize(query);
            first.IsValid.ShouldBeTrue(first.Diagnostics.FirstOrDefault()?.Message);
            QueryParseResult parsed = QueryTextParser.Parse(first.Text);
            parsed.IsValid.ShouldBeTrue(parsed.Diagnostics.FirstOrDefault()?.Message);
            QuerySerializationResult second = QueryTextSerializer.Serialize(parsed.Query);
            second.Text.ShouldBe(first.Text);
            QueryPlanJson.SerializeQuery(parsed.Query).ShouldBe(QueryPlanJson.SerializeQuery(query));
        }
    }

    [Fact]
    public void Unknown_and_malformed_inputs_are_stable_and_never_throw()
    {
        const string alphabet = "abcXYZ09:+-?|\\\" ";
        var random = new Random(0xBAD5EED);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            string input = new(Enumerable.Range(0, random.Next(0, 80))
                .Select(_ => alphabet[random.Next(alphabet.Length)])
                .ToArray());

            QueryParseResult first = QueryTextParser.Parse(input);
            QueryParseResult second = QueryTextParser.Parse(input);
            QueryPlanJson.SerializeQuery(second.Query).ShouldBe(QueryPlanJson.SerializeQuery(first.Query));
            JsonSerializer.Serialize(second.Diagnostics).ShouldBe(JsonSerializer.Serialize(first.Diagnostics));
        }
    }
}
