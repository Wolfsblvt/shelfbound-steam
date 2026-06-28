namespace Shelfbound.Query;

/// <summary>
/// The user's taste-profile / onboarding state, derived from the merged view. This is shared logic
/// (computed by <see cref="ProfileQuery"/>) that every surface reuses — the CLI, the MCP server, and
/// the future dashboard — while each renders it differently.
/// </summary>
public sealed record ProfileSummary
{
    /// <summary>True once there's any signal (a rating or a stated preference) to work from.</summary>
    public required bool IsSetUp { get; init; }

    public required int RatedGames { get; init; }
    public required int GamesWithStatus { get; init; }
    public required int GlobalPreferences { get; init; }
    public required int CategoryMeanings { get; init; }

    /// <summary>Category names the user uses but hasn't defined the meaning of yet (good to ask about).</summary>
    public required IReadOnlyList<string> UndefinedCategories { get; init; }

    /// <summary>Familiar (most-played / installed / recently-played) games the user hasn't rated yet.</summary>
    public required IReadOnlyList<SuggestedGame> SuggestedGamesToRate { get; init; }
}

/// <summary>A game worth asking the user to rate during onboarding.</summary>
public sealed record SuggestedGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public long? PlaytimeMinutes { get; init; }
}
