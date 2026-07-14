using System.Net;
using System.Text;
using Shelfbound.Steam.Web;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SteamWebApiClientTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 14, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Returns_positive_observations_with_response_freshness()
    {
        const string json =
            """{"response":{"games":[{"appid":20,"name":"Observed","playtime_forever":30,"rtime_last_played":1720000000}]}}""";
        var client = CreateClient(_ => Response(json));

        OwnedGamesResult result = await client.GetOwnedGamesAsync("steam-id", "api-key");

        result.Status.ShouldBe(OwnedGamesResultStatus.Usable);
        result.ObservedAt.ShouldBe(ObservedAt);
        OwnedGame game = result.Games.ShouldHaveSingleItem();
        game.AppId.ShouldBe(20);
        game.Name.ShouldBe("Observed");
        game.PlaytimeForeverMinutes.ShouldBe(30);
        game.LastPlayed.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1720000000));
        result.Warning.ShouldBeNull();
    }

    [Fact]
    public async Task Duplicate_appids_are_folded_into_one_deterministic_observation()
    {
        const string json =
            """{"response":{"games":[{"appid":20,"name":"App 20","playtime_forever":100,"rtime_last_played":1700000000},{"appid":10,"name":"Ten","playtime_forever":5},{"appid":20,"name":"Zed","playtime_forever":80},{"appid":20,"name":"Alpha","playtime_forever":30,"rtime_last_played":1720000000}]}}""";
        var client = CreateClient(_ => Response(json));

        OwnedGamesResult result = await client.GetOwnedGamesAsync("steam-id", "api-key");

        result.Games.Select(game => game.AppId).ShouldBe([10, 20]);
        OwnedGame merged = result.Games[1];
        merged.Name.ShouldBe("Alpha");
        merged.PlaytimeForeverMinutes.ShouldBe(100);
        merged.LastPlayed.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1720000000));
    }

    [Theory]
    [InlineData("{\"response\":{}}", OwnedGamesResultStatus.MissingGameList)]
    [InlineData("{\"response\":{\"games\":[]}}", OwnedGamesResultStatus.EmptyGameList)]
    [InlineData("{\"response\":{\"games\":{}}}", OwnedGamesResultStatus.MalformedResponse)]
    [InlineData("not-json", OwnedGamesResultStatus.MalformedResponse)]
    public async Task Preserves_unavailable_empty_and_malformed_response_states(
        string json,
        OwnedGamesResultStatus expectedStatus)
    {
        var client = CreateClient(_ => Response(json));

        OwnedGamesResult result = await client.GetOwnedGamesAsync("steam-id", "api-key");

        result.Status.ShouldBe(expectedStatus);
        result.ObservedAt.ShouldBe(ObservedAt);
        result.IsUsable.ShouldBeFalse();
        result.Games.ShouldBeEmpty();
        result.Warning.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Request_failures_do_not_expose_the_api_key()
    {
        const string apiKey = "never-log-this-key";
        var client = CreateClient(request =>
            throw new HttpRequestException($"Synthetic failure for {request.RequestUri}"));

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            () => client.GetOwnedGamesAsync("steam-id", apiKey));

        exception.ToString().ShouldNotContain(apiKey);
        exception.Message.ShouldContain("Steam Web API request failed");
    }

    private static SteamWebApiClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new HttpClient(new StubHandler(response)), new FixedClock(ObservedAt));

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
