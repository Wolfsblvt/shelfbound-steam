using Shelfbound.Core.Model;
using Shelfbound.Steam.Web;

namespace Shelfbound.Steam.Enrichment;

/// <summary>
/// Merges Steam Web API owned-games data into a locally scanned snapshot: sets playtime on installed
/// games and adds owned-but-not-installed games (with their local categories). Pure and deterministic
/// — the network fetch happens separately via <see cref="ISteamWebApiClient"/>, keeping this testable.
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
        var installedAppIds = snapshot.Games.Select(g => g.AppId).ToHashSet();
        var ownedByApp = ownedGames
            .GroupBy(o => o.AppId)
            .ToDictionary(g => g.Key, g => g.First());

        var games = new List<SnapshotGame>(snapshot.Games.Count + ownedGames.Count);

        // Installed games: attach playtime and last-played where the API knows them.
        foreach (var game in snapshot.Games)
        {
            games.Add(ownedByApp.TryGetValue(game.AppId, out OwnedGame? owned)
                ? game with { PlaytimeMinutes = owned.PlaytimeForeverMinutes, LastPlayed = owned.LastPlayed ?? game.LastPlayed }
                : game);
        }

        // Owned-but-not-installed games: add them with categories, playtime, and last-played.
        foreach (var owned in ownedGames)
        {
            if (installedAppIds.Contains(owned.AppId))
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

        return snapshot with { Games = games };
    }
}
