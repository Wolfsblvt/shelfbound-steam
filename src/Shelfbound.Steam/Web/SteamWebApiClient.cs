using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelfbound.Steam.Web;

/// <summary>
/// Steam Web API client for IPlayerService/GetOwnedGames. Requires a user-provided API key and a
/// public-enough profile. Never logs or stores the key.
/// </summary>
public sealed class SteamWebApiClient : ISteamWebApiClient
{
    private const string OwnedGamesUrl = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/";
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _clock;

    public SteamWebApiClient(HttpClient httpClient)
        : this(httpClient, TimeProvider.System)
    {
    }

    internal SteamWebApiClient(HttpClient httpClient, TimeProvider clock)
    {
        _httpClient = httpClient;
        _clock = clock;
    }

    public async Task<OwnedGamesResult> GetOwnedGamesAsync(
        string steamId64, string apiKey, CancellationToken cancellationToken = default)
    {
        string url = $"{OwnedGamesUrl}?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}" +
                     "&include_appinfo=1&include_played_free_games=1&format=json";

        Envelope? payload;
        DateTimeOffset observedAt;
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            observedAt = _clock.GetUtcNow();
            response.EnsureSuccessStatusCode();
            try
            {
                payload = await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                return Unavailable(OwnedGamesResultStatus.MalformedResponse, observedAt);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // HttpClient exceptions can carry the request URI, which contains the API key. Do not
            // retain them as an inner exception or surface their message to CLI/MCP consumers.
            throw new InvalidOperationException(
                "Steam Web API request failed. Check network access and Steam game-details visibility, then try again.");
        }

        List<ApiGame?>? games = payload?.Response?.Games;
        if (games is null)
            return Unavailable(OwnedGamesResultStatus.MissingGameList, observedAt);
        if (games.Count == 0)
            return Unavailable(OwnedGamesResultStatus.EmptyGameList, observedAt);

        OwnedGame[] observations = games
            .OfType<ApiGame>()
            .Where(game => game.AppId > 0)
            .Select(game => new OwnedGame
            {
                AppId = game.AppId,
                Name = string.IsNullOrWhiteSpace(game.Name) ? $"App {game.AppId}" : game.Name,
                PlaytimeForeverMinutes = Math.Max(0, game.PlaytimeForever),
                LastPlayed = ParseLastPlayed(game.RtimeLastPlayed),
            })
            .GroupBy(game => game.AppId)
            .Select(group => OwnedGame.Merge(group.Key, group))
            .OrderBy(game => game.AppId)
            .ToArray();

        return observations.Length == 0
            ? Unavailable(OwnedGamesResultStatus.MalformedResponse, observedAt)
            : new OwnedGamesResult
            {
                Status = OwnedGamesResultStatus.Usable,
                ObservedAt = observedAt,
                Games = observations,
            };
    }

    private static OwnedGamesResult Unavailable(OwnedGamesResultStatus status, DateTimeOffset observedAt) => new()
    {
        Status = status,
        ObservedAt = observedAt,
    };

    private static DateTimeOffset? ParseLastPlayed(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private sealed record Envelope([property: JsonPropertyName("response")] Payload? Response);

    private sealed record Payload([property: JsonPropertyName("games")] List<ApiGame?>? Games);

    private sealed record ApiGame(
        [property: JsonPropertyName("appid")] int AppId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("playtime_forever")] long PlaytimeForever,
        [property: JsonPropertyName("rtime_last_played")] long RtimeLastPlayed = 0);
}
