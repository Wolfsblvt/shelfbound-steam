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
}
