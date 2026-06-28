using System.ComponentModel;
using ModelContextProtocol.Server;
using Shelfbound.Core.Model;
using Shelfbound.Query;

namespace Shelfbound.Mcp.Tools;

/// <summary>
/// MCP tools over the user's Steam library snapshot. Read-only and deterministic; the AI client does
/// the reasoning. The <see cref="SnapshotContext"/> is injected from DI, not exposed as a tool input.
/// </summary>
[McpServerToolType]
public static class LibraryTools
{
    [McpServerTool(Name = "get_library_summary")]
    [Description("High-level overview of the user's Steam library: total/installed/categorized game counts, total size, total playtime (if available), libraries, and their categories (collections) with counts.")]
    public static LibrarySummary GetLibrarySummary(SnapshotContext context) =>
        LibraryQueryEngine.Summarize(context.Snapshot);

    [McpServerTool(Name = "get_categories")]
    [Description("List the user's local Steam categories (collections) with how many games each contains. These are the user's OWN labels (e.g. 'Deck', 'Hold'); if a category's meaning is unclear, ask the user what it means rather than guessing.")]
    public static IReadOnlyList<SnapshotCategory> GetCategories(SnapshotContext context) =>
        context.Snapshot.Categories;

    [McpServerTool(Name = "search_library")]
    [Description("Search and filter the library. All parameters are optional and combine with AND. Returns matching games with categories, install state, size, and playtime (when available).")]
    public static IReadOnlyList<SnapshotGame> SearchLibrary(
        SnapshotContext context,
        [Description("Case-insensitive substring match on the game name.")] string? text = null,
        [Description("Filter by install state: true = installed only, false = not installed.")] bool? installed = null,
        [Description("If true, only games with no category.")] bool? uncategorized = null,
        [Description("Match games in ANY of these categories.")] string[]? categoriesAny = null,
        [Description("Match games in ALL of these categories.")] string[]? categoriesAll = null,
        [Description("Exclude games in any of these categories.")] string[]? categoriesNone = null,
        [Description("Minimum total playtime in minutes (requires Steam Web API enrichment).")] long? minPlaytimeMinutes = null,
        [Description("Maximum total playtime in minutes.")] long? maxPlaytimeMinutes = null,
        [Description("Sort by: name | playtime | size | lastPlayed. Default name.")] string? sort = null,
        [Description("Sort descending.")] bool descending = false,
        [Description("Maximum number of results to return.")] int? limit = null)
    {
        var filter = new LibraryFilter
        {
            Text = text,
            Installed = installed,
            Uncategorized = uncategorized,
            CategoriesAny = categoriesAny,
            CategoriesAll = categoriesAll,
            CategoriesNone = categoriesNone,
            MinPlaytimeMinutes = minPlaytimeMinutes,
            MaxPlaytimeMinutes = maxPlaytimeMinutes,
            Sort = ParseSort(sort),
            Descending = descending,
            Limit = limit,
        };
        return LibraryQueryEngine.Search(context.Snapshot, filter);
    }

    [McpServerTool(Name = "get_game_details")]
    [Description("Get a single game by Steam app id, or by exact/closest name match.")]
    public static SnapshotGame? GetGameDetails(
        SnapshotContext context,
        [Description("Steam app id, if known.")] int? appId = null,
        [Description("Game name (case-insensitive; substring allowed).")] string? name = null)
    {
        var games = context.Snapshot.Games;
        if (appId is int id)
            return games.FirstOrDefault(g => g.AppId == id);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? games.FirstOrDefault(g => g.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    [McpServerTool(Name = "find_installed_unplayed")]
    [Description("Installed games the user likely hasn't started: never last-played and zero/unknown playtime. Good for 'what should I play next?'.")]
    public static IReadOnlyList<SnapshotGame> FindInstalledUnplayed(
        SnapshotContext context,
        [Description("Maximum number of results.")] int? limit = null)
    {
        return context.Snapshot.Games
            .Where(g => g.Installed && g.LastPlayed is null && (g.PlaytimeMinutes ?? 0) == 0)
            .OrderBy(g => g.Name)
            .Take(limit ?? int.MaxValue)
            .ToList();
    }

    private static LibrarySort ParseSort(string? sort) => sort?.ToLowerInvariant() switch
    {
        "playtime" or "playtimeminutes" => LibrarySort.PlaytimeMinutes,
        "size" or "sizeondiskbytes" => LibrarySort.SizeOnDiskBytes,
        "lastplayed" => LibrarySort.LastPlayed,
        _ => LibrarySort.Name,
    };
}
