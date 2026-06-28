using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shelfbound.Steam.Web;

/// <summary>
/// Steam Web API client for IPlayerService/GetOwnedGames. Requires a user-provided API key and a
/// public-enough profile. Never logs or stores the key.
/// </summary>
public sealed class SteamWebApiClient(HttpClient httpClient) : ISteamWebApiClient
{
    private const string OwnedGamesUrl = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/";

    public async Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(
        string steamId64, string apiKey, CancellationToken cancellationToken = default)
    {
        string url = $"{OwnedGamesUrl}?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}" +
                     "&include_appinfo=1&include_played_free_games=1&format=json";

        Envelope? payload = await httpClient.GetFromJsonAsync<Envelope>(url, cancellationToken);
        List<ApiGame>? games = payload?.Response?.Games;
        if (games is null)
            return [];

        return games
            .Select(g => new OwnedGame
            {
                AppId = g.AppId,
                Name = string.IsNullOrWhiteSpace(g.Name) ? $"App {g.AppId}" : g.Name,
                PlaytimeForeverMinutes = g.PlaytimeForever,
                LastPlayed = g.RtimeLastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.RtimeLastPlayed) : null,
            })
            .ToList();
    }

    private sealed record Envelope([property: JsonPropertyName("response")] Payload? Response);

    private sealed record Payload([property: JsonPropertyName("games")] List<ApiGame>? Games);

    private sealed record ApiGame(
        [property: JsonPropertyName("appid")] int AppId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("playtime_forever")] long PlaytimeForever,
        [property: JsonPropertyName("rtime_last_played")] long RtimeLastPlayed = 0);
}
