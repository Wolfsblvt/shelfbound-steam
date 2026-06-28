namespace Shelfbound.Query;

/// <summary>
/// Derives the user's profile/onboarding state from the merged <see cref="LibraryView"/>. Shared,
/// deterministic logic so the CLI, MCP server, and dashboard all compute "what's set up / what to ask"
/// the same way; only the presentation differs per surface.
/// </summary>
public static class ProfileQuery
{
    public static ProfileSummary Summarize(LibraryView view, int suggestionLimit = 10)
    {
        int rated = view.Games.Count(g => g.Rating is not null);
        int withStatus = view.Games.Count(g => g.Status is not null);

        var undefinedCategories = view.Categories
            .Select(c => c.Name)
            .Where(name => !view.CategoryDefinitions.ContainsKey(name))
            .ToList();

        var suggestions = view.Games
            .Where(g => g.Rating is null)
            .OrderByDescending(g => g.PlaytimeMinutes ?? 0)
            .ThenByDescending(g => g.Installed)
            .ThenByDescending(g => g.Snapshot.LastPlayed ?? DateTimeOffset.MinValue)
            .ThenBy(g => g.Name)
            .Take(suggestionLimit)
            .Select(g => new SuggestedGame { AppId = g.AppId, Name = g.Name, PlaytimeMinutes = g.PlaytimeMinutes })
            .ToList();

        return new ProfileSummary
        {
            IsSetUp = rated > 0 || view.GlobalMemories.Count > 0,
            RatedGames = rated,
            GamesWithStatus = withStatus,
            GlobalPreferences = view.GlobalMemories.Count,
            CategoryMeanings = view.CategoryDefinitions.Count,
            UndefinedCategories = undefinedCategories,
            SuggestedGamesToRate = suggestions,
        };
    }
}
