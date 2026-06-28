using System.ComponentModel;
using ModelContextProtocol.Server;
using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;
using Shelfbound.Storage;

namespace Shelfbound.Mcp.Tools;

/// <summary>
/// MCP read tools over the merged library view (snapshot facts + the user's saved data). Read-only and
/// deterministic; the AI client does the reasoning. <see cref="SnapshotContext"/> and the user-data
/// store are injected from DI, not exposed as tool inputs.
/// </summary>
[McpServerToolType]
public static class LibraryTools
{
    private static LibraryView View(SnapshotContext context, IUserDataStore store) =>
        LibraryViewBuilder.Build(context.Snapshot, store.Load(context.OwnerId));

    [McpServerTool(Name = "get_library_summary")]
    [Description("High-level overview of the library: total/installed/categorized game counts, total size, total playtime (if available), libraries, and categories (collections) with counts.")]
    public static LibrarySummary GetLibrarySummary(SnapshotContext context, IUserDataStore store) =>
        LibraryQueryEngine.Summarize(View(context, store));

    [McpServerTool(Name = "get_categories")]
    [Description("List the user's local Steam categories (collections) with how many games each contains. These are the user's OWN labels (e.g. 'Deck', 'Hold'); if a meaning is unclear, ask the user rather than guessing.")]
    public static IReadOnlyList<SnapshotCategory> GetCategories(SnapshotContext context) =>
        context.Snapshot.Categories;

    [McpServerTool(Name = "search_library")]
    [Description("Search/filter the library by facts AND the user's saved data. All parameters are optional and combine with AND. Returns games with categories, install state, size, playtime, and any saved status/rating/completion/memories.")]
    public static IReadOnlyList<LibraryGame> SearchLibrary(
        SnapshotContext context, IUserDataStore store,
        [Description("Case-insensitive substring match on the game name.")] string? text = null,
        [Description("true = installed only, false = not installed.")] bool? installed = null,
        [Description("If true, only games with no category.")] bool? uncategorized = null,
        [Description("Match games in ANY of these categories.")] string[]? categoriesAny = null,
        [Description("Match games in ALL of these categories.")] string[]? categoriesAll = null,
        [Description("Exclude games in any of these categories.")] string[]? categoriesNone = null,
        [Description("Minimum total playtime in minutes (needs Steam Web API enrichment).")] long? minPlaytimeMinutes = null,
        [Description("Maximum total playtime in minutes.")] long? maxPlaytimeMinutes = null,
        [Description("Saved status: wantToPlay|playing|paused|finished|dropped|replayable|comfortGame|ignored|playedElsewhere.")] string? status = null,
        [Description("Saved rating: loved|liked|mixed|disliked|neverAgain.")] string? rating = null,
        [Description("Minimum completion percent (0-100).")] int? minCompletionPercent = null,
        [Description("Maximum completion percent (0-100).")] int? maxCompletionPercent = null,
        [Description("Filter on whether the user played it elsewhere.")] bool? playedElsewhere = null,
        [Description("Sort by: name | playtime | size | lastPlayed | completion. Default name.")] string? sort = null,
        [Description("Sort descending.")] bool descending = false,
        [Description("Maximum number of results.")] int? limit = null)
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
            Status = ParseEnum<GameStatus>(status),
            Rating = ParseEnum<GameRating>(rating),
            MinCompletionPercent = minCompletionPercent,
            MaxCompletionPercent = maxCompletionPercent,
            PlayedElsewhere = playedElsewhere,
            Sort = ParseSort(sort),
            Descending = descending,
            Limit = limit,
        };
        return LibraryQueryEngine.Search(View(context, store), filter);
    }

    [McpServerTool(Name = "get_game_details")]
    [Description("Get a single game — facts plus the user's saved data and game-scoped memories — by Steam app id or by exact/closest name match.")]
    public static LibraryGame? GetGameDetails(
        SnapshotContext context, IUserDataStore store,
        [Description("Steam app id, if known.")] int? appId = null,
        [Description("Game name (case-insensitive; substring allowed).")] string? name = null)
    {
        var games = View(context, store).Games;
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
    [Description("Installed games the user likely hasn't started: never last-played, zero/unknown playtime, and not already marked finished/dropped/ignored/played-elsewhere. Good for 'what should I play next?'.")]
    public static IReadOnlyList<LibraryGame> FindInstalledUnplayed(
        SnapshotContext context, IUserDataStore store,
        [Description("Maximum number of results.")] int? limit = null)
    {
        GameStatus[] done = [GameStatus.Finished, GameStatus.Dropped, GameStatus.Ignored, GameStatus.PlayedElsewhere];
        return View(context, store).Games
            .Where(g => g.Installed
                && g.Snapshot.LastPlayed is null
                && (g.PlaytimeMinutes ?? 0) == 0
                && (g.Status is null || !done.Contains(g.Status.Value)))
            .OrderBy(g => g.Name)
            .Take(limit ?? int.MaxValue)
            .ToList();
    }

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        value is not null && Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : null;

    private static LibrarySort ParseSort(string? sort) => sort?.ToLowerInvariant() switch
    {
        "playtime" or "playtimeminutes" => LibrarySort.PlaytimeMinutes,
        "size" or "sizeondiskbytes" => LibrarySort.SizeOnDiskBytes,
        "lastplayed" => LibrarySort.LastPlayed,
        "completion" or "completionpercent" => LibrarySort.CompletionPercent,
        _ => LibrarySort.Name,
    };
}
