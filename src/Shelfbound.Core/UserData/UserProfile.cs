namespace Shelfbound.Core.UserData;

/// <summary>
/// The root user-data document for one owner (profile). Locally there is a single profile (the machine
/// owner); the hosted layer maps a profile to an authenticated account over the same model. Mutable
/// collections so callers can update in place inside a store transaction.
/// </summary>
public sealed record UserProfile
{
    public required string OwnerId { get; init; }

    /// <summary>Per-game structured data, keyed by Steam app id.</summary>
    public Dictionary<int, GameUserData> Games { get; init; } = [];

    /// <summary>Scoped durable facts (global / game / category).</summary>
    public List<Memory> Memories { get; init; } = [];

    /// <summary>The user's meanings for their category names, keyed by category name.</summary>
    public Dictionary<string, CategoryDefinition> CategoryDefinitions { get; init; } = [];

    /// <summary>When Shelfbound first scanned this owner's library (the baseline for "recently added").</summary>
    public DateTimeOffset? FirstScanAt { get; set; }

    /// <summary>First time each app id was observed owned — a proxy for when it was added/bought.</summary>
    public Dictionary<int, DateTimeOffset> FirstSeen { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
