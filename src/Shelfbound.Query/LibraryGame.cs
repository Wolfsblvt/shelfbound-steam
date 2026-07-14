using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>
/// A single game's merged view: the snapshot facts plus the user's data (status/rating/completion/…)
/// and its game-scoped memories. Query and (later) the dashboard operate on this, so facts and
/// user-data are filtered together rather than living in separate worlds.
/// </summary>
public sealed record LibraryGame
{
    public required SnapshotGame Snapshot { get; init; }
    public GameUserData? UserData { get; init; }
    public IReadOnlyList<Memory> Memories { get; init; } = [];

    // Convenience accessors for filtering/sorting.
    public int AppId => Snapshot.AppId;
    public string Name => Snapshot.Name;
    public bool Installed => Snapshot.Installed;
    public long? PlaytimeMinutes => Snapshot.PlaytimeMinutes;
    public IReadOnlyList<string> Categories => Snapshot.Categories;
    public GameStatus? Status => UserData?.Status;
    public GameRating? Rating => UserData?.Rating;
    public int? CompletionPercent => UserData?.CompletionPercent;
    public bool PlayedElsewhere => UserData?.PlayedElsewhere ?? false;

    /// <summary>
    /// Conservative first-observation time. It is a baseline for partial coverage and may date a post-baseline
    /// addition only under stable complete coverage; it is never a purchase timestamp by itself.
    /// </summary>
    public DateTimeOffset? FirstSeenAt { get; init; }

    // Recency as human phrases (models weight "3 days ago" over a raw date).
    /// <summary>"N ago" for games newly added since the baseline scan; null for baseline/older games.</summary>
    public string? AddedAgo { get; init; }
    public string? InstalledOrUpdatedAgo => RelativeTime.Describe(Snapshot.LastUpdated);
    public string? LastPlayedAgo => Snapshot.LastPlayed is null ? "never" : RelativeTime.Describe(Snapshot.LastPlayed);
}
