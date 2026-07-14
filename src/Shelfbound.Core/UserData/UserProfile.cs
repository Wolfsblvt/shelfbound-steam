using Shelfbound.Core.Model;

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

    /// <summary>
    /// Conservative first-observation timestamps. A timestamp after <see cref="FirstScanAt"/> is only
    /// recorded when a stable complete source can support acquisition recency.
    /// </summary>
    public Dictionary<int, DateTimeOffset> FirstSeen { get; init; } = [];

    /// <summary>
    /// The widest scan coverage observed so far (a high-water mark). Comparisons use
    /// <see cref="LibraryScopeSemantics"/>, never enum ordinals. A broader scan reveals games without
    /// proving when they were acquired, so those games are baselined instead of dated. Defaults to
    /// <see cref="LibraryScope.InstalledOnly"/> so legacy profiles remain conservative.
    /// </summary>
    public LibraryScope WidestScanScope { get; set; } = LibraryScope.InstalledOnly;

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
