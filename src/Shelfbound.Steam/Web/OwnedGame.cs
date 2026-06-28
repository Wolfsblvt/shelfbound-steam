namespace Shelfbound.Steam.Web;

/// <summary>A game the user owns, as reported by the Steam Web API (IPlayerService/GetOwnedGames).</summary>
public sealed record OwnedGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required long PlaytimeForeverMinutes { get; init; }
}
