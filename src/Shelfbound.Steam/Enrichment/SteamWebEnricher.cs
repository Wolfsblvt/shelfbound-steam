using Shelfbound.Core.Model;
using Shelfbound.Steam.Web;

namespace Shelfbound.Steam.Enrichment;

/// <summary>
/// Merges positive visibility-gated Steam Web API observations into a locally scanned snapshot: sets
/// playtime on installed games and adds visible not-installed game observations (with local categories).
/// Pure and deterministic — the network fetch happens separately via <see cref="ISteamWebApiClient"/>.
///
/// The document-level category summary is already computed from the full local category map, so it
/// is left unchanged here.
/// </summary>
public static class SteamWebEnricher
{
    public static SnapshotDocument Enrich(
        SnapshotDocument snapshot,
        IReadOnlyDictionary<int, IReadOnlyList<string>> categoriesByApp,
        IReadOnlyList<OwnedGame> ownedGames)
    {
        var ownedByApp = ownedGames
            .Where(game => game.AppId > 0)
            .GroupBy(o => o.AppId)
            .ToDictionary(group => group.Key, group => OwnedGame.Merge(group.Key, group));

        if (ownedByApp.Count == 0)
            return snapshot;

        var localGames = snapshot.Games
            .Select((game, index) => new IndexedGame(game, index))
            .GroupBy(item => item.Game.AppId)
            .OrderBy(group => group.Min(item => item.Index))
            .Select(group => MergeLocalObservations(group.Select(item => item.Game)))
            .ToList();
        var localAppIds = localGames.Select(game => game.AppId).ToHashSet();
        var games = new List<SnapshotGame>(localGames.Count + ownedByApp.Count);

        // Installed games: attach playtime and last-played where the API knows them.
        foreach (SnapshotGame game in localGames)
        {
            games.Add(ownedByApp.TryGetValue(game.AppId, out OwnedGame? owned)
                ? game with { PlaytimeMinutes = owned.PlaytimeForeverMinutes, LastPlayed = owned.LastPlayed ?? game.LastPlayed }
                : game);
        }

        // Visible not-installed observations: add them with categories and playtime.
        foreach (OwnedGame owned in ownedByApp.Values.OrderBy(game => game.AppId))
        {
            if (localAppIds.Contains(owned.AppId))
                continue;

            games.Add(new SnapshotGame
            {
                AppId = owned.AppId,
                Name = owned.Name,
                Installed = false,
                LibraryIndex = null,
                PlaytimeMinutes = owned.PlaytimeForeverMinutes,
                LastPlayed = owned.LastPlayed,
                Categories = categoriesByApp.TryGetValue(owned.AppId, out var cats) ? cats : [],
            });
        }

        // GetOwnedGames is visibility-gated and supplies only positive observations. Even a successful
        // response has no completeness contract, so absence from the enriched result proves nothing.
        return snapshot with
        {
            Games = games,
            Stats = snapshot.Stats with
            {
                Scope = LibraryScopeSemantics.BroaderOf(
                    LibraryScopeSemantics.GetOperationalScope(snapshot.SchemaVersion, snapshot.Stats.Scope),
                    LibraryScope.ObservedSubset),
            },
        };
    }

    private static SnapshotGame MergeLocalObservations(IEnumerable<SnapshotGame> observations)
    {
        SnapshotGame[] games = observations.ToArray();
        SnapshotGame winner = games
            .OrderByDescending(game => game.Installed)
            .ThenBy(game => game.LibraryIndex ?? int.MaxValue)
            .ThenBy(game => game.InstallDir ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(game => game.Name, StringComparer.Ordinal)
            .First();
        bool installed = games.Any(game => game.Installed);

        return winner with
        {
            Installed = installed,
            LibraryIndex = installed ? winner.LibraryIndex : null,
            InstallDir = installed ? winner.InstallDir : null,
            SizeOnDiskBytes = installed ? games.Max(game => game.SizeOnDiskBytes) : null,
            PlaytimeMinutes = games.Max(game => game.PlaytimeMinutes),
            LastUpdated = games.Max(game => game.LastUpdated),
            LastPlayed = games.Max(game => game.LastPlayed),
            Categories = games
                .SelectMany(game => game.Categories)
                .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(category => category, StringComparer.Ordinal).First())
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(category => category, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private sealed record IndexedGame(SnapshotGame Game, int Index);
}
