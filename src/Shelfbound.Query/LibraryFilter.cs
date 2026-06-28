namespace Shelfbound.Query;

/// <summary>How to sort library query results.</summary>
public enum LibrarySort
{
    Name,
    PlaytimeMinutes,
    SizeOnDiskBytes,
    LastPlayed,
}

/// <summary>
/// A composable, deterministic filter over the library. Every field is optional; null means "no
/// constraint". Designed to grow (status, device, tags, …) without breaking callers.
/// </summary>
public sealed record LibraryFilter
{
    public string? Text { get; init; }
    public bool? Installed { get; init; }
    public bool? Uncategorized { get; init; }
    public IReadOnlyList<string>? CategoriesAny { get; init; }
    public IReadOnlyList<string>? CategoriesAll { get; init; }
    public IReadOnlyList<string>? CategoriesNone { get; init; }
    public long? MinPlaytimeMinutes { get; init; }
    public long? MaxPlaytimeMinutes { get; init; }
    public LibrarySort Sort { get; init; } = LibrarySort.Name;
    public bool Descending { get; init; }
    public int? Limit { get; init; }
}
