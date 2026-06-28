namespace Shelfbound.Core.UserData;

/// <summary>
/// Per-game user/derived data layered on top of the snapshot's factual data. Flexible by design:
/// structured fields for the common cases, plus a <see cref="Custom"/> bag (and scoped memories on
/// <see cref="UserProfile"/>) for anything not modeled explicitly yet.
/// </summary>
public sealed record GameUserData
{
    public required int AppId { get; init; }

    public GameStatus? Status { get; init; }
    public GameRating? Rating { get; init; }

    /// <summary>Completion level 0-100, if known.</summary>
    public int? CompletionPercent { get; init; }

    /// <summary>True if the user finished/played the game on another platform.</summary>
    public bool? PlayedElsewhere { get; init; }

    /// <summary>Taste signals the user gave (e.g. "dark themes", "meaningful choices").</summary>
    public List<string> LikedAspects { get; init; } = [];
    public List<string> DislikedAspects { get; init; } = [];

    /// <summary>Extension point for data not modeled explicitly yet. Keeps the schema flexible.</summary>
    public Dictionary<string, string> Custom { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
