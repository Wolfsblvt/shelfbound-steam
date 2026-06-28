using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>How to sort library query results.</summary>
public enum LibrarySort
{
    Name,
    PlaytimeMinutes,
    SizeOnDiskBytes,
    LastPlayed,
    CompletionPercent,
}

/// <summary>
/// A composable, deterministic filter over the merged library (facts + user-data). Every field is
/// optional; null means "no constraint". Designed to grow without breaking callers.
/// </summary>
public sealed record LibraryFilter
{
    // Snapshot facts
    public string? Text { get; init; }
    public bool? Installed { get; init; }
    public bool? Uncategorized { get; init; }
    public IReadOnlyList<string>? CategoriesAny { get; init; }
    public IReadOnlyList<string>? CategoriesAll { get; init; }
    public IReadOnlyList<string>? CategoriesNone { get; init; }
    public long? MinPlaytimeMinutes { get; init; }
    public long? MaxPlaytimeMinutes { get; init; }

    // User data
    public GameStatus? Status { get; init; }
    public GameRating? Rating { get; init; }
    public int? MinCompletionPercent { get; init; }
    public int? MaxCompletionPercent { get; init; }
    public bool? PlayedElsewhere { get; init; }

    public LibrarySort Sort { get; init; } = LibrarySort.Name;
    public bool Descending { get; init; }
    public int? Limit { get; init; }
}
