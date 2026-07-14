namespace Shelfbound.Steam.Web;

/// <summary>A positive visibility-gated owned-game observation from IPlayerService/GetOwnedGames.</summary>
public sealed record OwnedGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required long PlaytimeForeverMinutes { get; init; }

    /// <summary>Last time the user played it, per the API (rtime_last_played); null if never/unknown.</summary>
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>Deterministically folds duplicate observations for one appid into their strongest useful facts.</summary>
    internal static OwnedGame Merge(int appId, IEnumerable<OwnedGame> observations)
    {
        OwnedGame[] rows = observations.ToArray();
        string fallbackName = $"App {appId}";
        string name = rows
            .Select(game => game.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => string.Equals(value, fallbackName, StringComparison.Ordinal))
            .ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? fallbackName;

        return new OwnedGame
        {
            AppId = appId,
            Name = name,
            PlaytimeForeverMinutes = rows.Max(game => game.PlaytimeForeverMinutes),
            LastPlayed = rows.Max(game => game.LastPlayed),
        };
    }
}
