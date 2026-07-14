namespace Shelfbound.Steam.Web;

/// <summary>A positive visibility-gated owned-game observation from IPlayerService/GetOwnedGames.</summary>
public sealed record OwnedGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required long PlaytimeForeverMinutes { get; init; }

    /// <summary>Last time the user played it, per the API (rtime_last_played); null if never/unknown.</summary>
    public DateTimeOffset? LastPlayed { get; init; }
}
