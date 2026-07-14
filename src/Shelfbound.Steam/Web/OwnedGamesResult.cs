namespace Shelfbound.Steam.Web;

/// <summary>Outcome of a visibility-gated Steam owned-games request.</summary>
public enum OwnedGamesResultStatus
{
    /// <summary>The response contained one or more usable positive game observations.</summary>
    Usable = 0,

    /// <summary>The response did not contain a game-list member.</summary>
    MissingGameList,

    /// <summary>The response contained an explicit but non-authoritative empty game list.</summary>
    EmptyGameList,

    /// <summary>The response body could not be read as the documented owned-games shape.</summary>
    MalformedResponse,
}

/// <summary>
/// Visibility-gated Steam owned-game observations plus the time the response was received. Only
/// <see cref="OwnedGamesResultStatus.Usable"/> carries positive observations; every other state is
/// unavailable evidence, not a successful empty or complete library.
/// </summary>
public sealed record OwnedGamesResult
{
    public required OwnedGamesResultStatus Status { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public IReadOnlyList<OwnedGame> Games { get; init; } = [];

    public bool IsUsable => Status == OwnedGamesResultStatus.Usable;

    /// <summary>Actionable, credential-free text suitable for CLI warnings and MCP logs.</summary>
    public string? Warning => Status switch
    {
        OwnedGamesResultStatus.Usable => null,
        OwnedGamesResultStatus.MissingGameList =>
            "Steam did not return an owned-games list. Check Steam game-details visibility and try again; continuing with installed games only.",
        OwnedGamesResultStatus.EmptyGameList =>
            "Steam returned an empty owned-games list, which Shelfbound cannot treat as complete. Check Steam game-details visibility and try again; continuing with installed games only.",
        OwnedGamesResultStatus.MalformedResponse =>
            "Steam returned an unreadable owned-games response. Try again later; continuing with installed games only.",
        _ => throw new ArgumentOutOfRangeException(nameof(Status), Status, "Unknown owned-games result status."),
    };
}
