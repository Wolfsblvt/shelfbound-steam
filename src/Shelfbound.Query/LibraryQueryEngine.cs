using Shelfbound.Core.Model;

namespace Shelfbound.Query;

/// <summary>
/// Deterministic filtering, sorting, and summarizing over a snapshot's games. No I/O, no LLM — this is
/// the shared library logic reused by the MCP server now and the dashboard/hosted layer later.
/// </summary>
public static class LibraryQueryEngine
{
    public static IReadOnlyList<SnapshotGame> Search(SnapshotDocument snapshot, LibraryFilter filter)
    {
        IEnumerable<SnapshotGame> games = snapshot.Games;

        if (!string.IsNullOrWhiteSpace(filter.Text))
            games = games.Where(g => g.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase));

        if (filter.Installed is bool installed)
            games = games.Where(g => g.Installed == installed);

        if (filter.Uncategorized is true)
            games = games.Where(g => g.Categories.Count == 0);

        if (filter.CategoriesAny is { Count: > 0 } any)
            games = games.Where(g => g.Categories.Any(c => Contains(any, c)));

        if (filter.CategoriesAll is { Count: > 0 } all)
            games = games.Where(g => all.All(required => Contains(g.Categories, required)));

        if (filter.CategoriesNone is { Count: > 0 } none)
            games = games.Where(g => !g.Categories.Any(c => Contains(none, c)));

        if (filter.MinPlaytimeMinutes is long min)
            games = games.Where(g => (g.PlaytimeMinutes ?? 0) >= min);

        if (filter.MaxPlaytimeMinutes is long max)
            games = games.Where(g => (g.PlaytimeMinutes ?? 0) <= max);

        games = SortGames(games, filter.Sort, filter.Descending);

        if (filter.Limit is int limit and > 0)
            games = games.Take(limit);

        return games.ToList();
    }

    public static LibrarySummary Summarize(SnapshotDocument snapshot)
    {
        var games = snapshot.Games;
        bool anyPlaytime = games.Any(g => g.PlaytimeMinutes is not null);
        return new LibrarySummary
        {
            TotalGames = games.Count,
            InstalledGames = games.Count(g => g.Installed),
            CategorizedGames = games.Count(g => g.Categories.Count > 0),
            TotalSizeOnDiskBytes = games.Sum(g => g.SizeOnDiskBytes ?? 0),
            TotalPlaytimeMinutes = anyPlaytime ? games.Sum(g => g.PlaytimeMinutes ?? 0) : null,
            Categories = snapshot.Categories,
            Libraries = snapshot.Libraries,
        };
    }

    private static bool Contains(IEnumerable<string> categories, string name) =>
        categories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<SnapshotGame> SortGames(IEnumerable<SnapshotGame> games, LibrarySort sort, bool descending)
    {
        Func<SnapshotGame, IComparable> key = sort switch
        {
            LibrarySort.PlaytimeMinutes => g => g.PlaytimeMinutes ?? 0,
            LibrarySort.SizeOnDiskBytes => g => g.SizeOnDiskBytes ?? 0,
            LibrarySort.LastPlayed => g => g.LastPlayed?.UtcDateTime ?? DateTime.MinValue,
            _ => g => g.Name,
        };
        return descending ? games.OrderByDescending(key) : games.OrderBy(key);
    }
}
