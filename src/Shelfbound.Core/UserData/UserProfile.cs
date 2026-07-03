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

    /// <summary>First time each app id was observed owned — a proxy for when it was added/bought.</summary>
    public Dictionary<int, DateTimeOffset> FirstSeen { get; init; } = [];

    /// <summary>
    /// The widest scan coverage observed so far (a high-water mark). When a later scan is broader than
    /// this (e.g. <see cref="LibraryScope.InstalledOnly"/> → <see cref="LibraryScope.FullLibrary"/>), the
    /// previously-owned games it reveals are newly <em>visible</em>, not newly <em>added</em>, so they're
    /// baselined instead of dated. Defaults to <see cref="LibraryScope.InstalledOnly"/> (the conservative
    /// assumption, matching <see cref="Model.SnapshotStats.Scope"/>) so a legacy profile lacking this
    /// field never treats a later full scan as a wave of genuine acquisitions.
    /// </summary>
    public LibraryScope WidestScanScope { get; set; } = LibraryScope.InstalledOnly;

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
