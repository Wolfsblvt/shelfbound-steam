using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>
/// Deterministic filtering, sorting, and summarizing over the merged <see cref="LibraryView"/> (facts +
/// user-data). No I/O, no LLM — the shared library logic reused by the MCP server now and the
/// dashboard/hosted layer later. Build a view with <see cref="LibraryViewBuilder"/>.
/// </summary>
public static class LibraryQueryEngine
{
    public static IReadOnlyList<LibraryGame> Search(LibraryView view, LibraryFilter filter)
    {
        IEnumerable<LibraryGame> games = view.Games;

        // Snapshot facts.
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

        // User data.
        if (filter.Status is GameStatus status)
            games = games.Where(g => g.Status == status);
        if (filter.Rating is GameRating rating)
            games = games.Where(g => g.Rating == rating);
        if (filter.MinCompletionPercent is int minCompletion)
            games = games.Where(g => (g.CompletionPercent ?? 0) >= minCompletion);
        if (filter.MaxCompletionPercent is int maxCompletion)
            games = games.Where(g => (g.CompletionPercent ?? 0) <= maxCompletion);
        if (filter.PlayedElsewhere is bool playedElsewhere)
            games = games.Where(g => g.PlayedElsewhere == playedElsewhere);

        games = SortGames(games, filter.Sort, filter.Descending);

        if (filter.Limit is int limit and > 0)
            games = games.Take(limit);

        return games.ToList();
    }

    public static LibrarySummary Summarize(LibraryView view)
    {
        var games = view.Games;
        bool anyPlaytime = games.Any(g => g.PlaytimeMinutes is not null);
        return new LibrarySummary
        {
            TotalGames = games.Count,
            InstalledGames = games.Count(g => g.Installed),
            CategorizedGames = games.Count(g => g.Categories.Count > 0),
            TotalSizeOnDiskBytes = games.Sum(g => g.Snapshot.SizeOnDiskBytes ?? 0),
            TotalPlaytimeMinutes = anyPlaytime ? games.Sum(g => g.PlaytimeMinutes ?? 0) : null,
            Categories = view.Categories,
            Libraries = view.Libraries,
            Scope = view.Scope,
        };
    }

    private static bool Contains(IEnumerable<string> categories, string name) =>
        categories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<LibraryGame> SortGames(IEnumerable<LibraryGame> games, LibrarySort sort, bool descending)
    {
        Func<LibraryGame, IComparable> key = sort switch
        {
            LibrarySort.PlaytimeMinutes => g => g.PlaytimeMinutes ?? 0,
            LibrarySort.SizeOnDiskBytes => g => g.Snapshot.SizeOnDiskBytes ?? 0,
            LibrarySort.LastPlayed => g => g.Snapshot.LastPlayed?.UtcDateTime ?? DateTime.MinValue,
            LibrarySort.CompletionPercent => g => g.CompletionPercent ?? 0,
            _ => g => g.Name,
        };
        return descending ? games.OrderByDescending(key) : games.OrderBy(key);
    }
}
