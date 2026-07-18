using System.Text.Json;
using Shelfbound.Query;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class QueryPlanCorpusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void G1_through_G25_match_the_public_canonical_corpus()
    {
        QueryCorpus corpus = LoadCorpus();
        corpus.Contract.ShouldBe("shelfbound.query-plan");
        corpus.Version.ShouldBe(QueryPlan.CurrentVersion);
        corpus.Cases.Count.ShouldBe(41);
        corpus.Cases.Select(item => item.Golden).Distinct().OrderBy(GoldenNumber)
            .ShouldBe(Enumerable.Range(1, 25).Select(number => $"G{number}").ToArray());

        foreach (QueryCorpusCase item in corpus.Cases)
        {
            QueryParseResult parsed = QueryTextParser.Parse(item.Input);
            QueryPlanJson.SerializeQuery(parsed.Query).ShouldBe(
                QueryPlanJson.SerializeQuery(item.Expect.Query), $"{item.Id}: canonical query");
            Serialize(parsed.Diagnostics).ShouldBe(
                Serialize(item.Expect.Diagnostics), $"{item.Id}: structured diagnostics");

            QueryParseResult repeated = QueryTextParser.Parse(item.Input);
            QueryPlanJson.SerializeQuery(repeated.Query).ShouldBe(
                QueryPlanJson.SerializeQuery(parsed.Query), $"{item.Id}: deterministic query");
            Serialize(repeated.Diagnostics).ShouldBe(
                Serialize(parsed.Diagnostics), $"{item.Id}: deterministic diagnostics");

            QueryParseResult hosted = QueryTextParser.Parse(item.Input, QueryCapabilityTarget.Hosted);
            Serialize(hosted.Diagnostics).ShouldBe(
                Serialize(parsed.Diagnostics), $"{item.Id}: hosted grammar parity");

            QueryParseResult local = QueryTextParser.Parse(item.Input, QueryCapabilityTarget.Local);
            IReadOnlyList<QueryCapabilityExpectation> capabilities = local.Diagnostics
                .Where(diagnostic => diagnostic.Code is "unsupported_facet" or "unsupported_operator")
                .Select(diagnostic => new QueryCapabilityExpectation(
                    diagnostic.Code,
                    diagnostic.Field,
                    diagnostic.Facet))
                .ToList();
            Serialize(capabilities).ShouldBe(
                Serialize(item.Expect.LocalCapabilities), $"{item.Id}: local capability classification");

            if (item.Expect.Diagnostics.Count > 0)
            {
                item.Expect.CanonicalText.ShouldBeNull($"{item.Id}: invalid cases do not claim canonical text");
                continue;
            }

            QuerySerializationResult serialized = QueryTextSerializer.Serialize(parsed.Query);
            serialized.IsValid.ShouldBeTrue($"{item.Id}: {serialized.Diagnostics.FirstOrDefault()?.Message}");
            serialized.Text.ShouldBe(item.Expect.CanonicalText, $"{item.Id}: canonical text");

            QueryParseResult roundTripped = QueryTextParser.Parse(serialized.Text);
            roundTripped.IsValid.ShouldBeTrue($"{item.Id}: canonical text must parse cleanly");
            QueryPlanJson.SerializeQuery(roundTripped.Query).ShouldBe(
                QueryPlanJson.SerializeQuery(parsed.Query), $"{item.Id}: parse/serialize/parse identity");
        }
    }

    private static int GoldenNumber(string value) => int.Parse(value.AsSpan(1));

    private static QueryCorpus LoadCorpus()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Contracts",
            "power-search",
            "query-plan-v1.corpus.json");
        return JsonSerializer.Deserialize<QueryCorpus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("The QueryPlan v1 corpus cannot be null.");
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed record QueryCorpus(string Contract, int Version, IReadOnlyList<QueryCorpusCase> Cases);

    private sealed record QueryCorpusCase(
        string Id,
        string Golden,
        string Input,
        QueryCorpusExpectation Expect,
        IReadOnlyList<string> Future);

    private sealed record QueryCorpusExpectation(
        QueryPlanQuery Query,
        string? CanonicalText,
        IReadOnlyList<QueryDiagnostic> Diagnostics,
        IReadOnlyList<QueryCapabilityExpectation> LocalCapabilities);

    private sealed record QueryCapabilityExpectation(string Code, string Field, string? Facet);
}
