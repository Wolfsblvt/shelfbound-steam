using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>One suggested game within a card, with a short human reason.</summary>
public sealed record RecommendationItem(int AppId, string Name, string Why);

/// <summary>A themed recommendation surface (e.g. "Play next on your Deck") with its games.</summary>
public sealed record RecommendationCard(string Id, string Title, string Subtitle, IReadOnlyList<RecommendationItem> Items);

/// <summary>
/// Deterministic recommendation cards over a <see cref="LibraryView"/> — the engine behind "what should I
/// play next?" in the MCP tools and (later) the dashboard. Device-aware: it only frames suggestions for a
/// device when the view's device IS that device, so it never says "play on your Deck" unless it knows
/// there's a Deck. Uses only data we already have (install state, playtime, status, recency, size);
/// completion-time / compatibility enrichment will make it richer later.
/// </summary>
public static class RecommendationEngine
{
    private static readonly GameStatus[] DoneStatuses =
        [GameStatus.Finished, GameStatus.Dropped, GameStatus.Ignored, GameStatus.PlayedElsewhere];

    public static IReadOnlyList<RecommendationCard> Build(LibraryView view, int perCard = 6)
    {
        var cards = new List<RecommendationCard>();
        IReadOnlyList<LibraryGame> games = view.Games;
        bool onDeck = view.Device?.Type == DeviceType.SteamDeck;

        // Installed but unplayed — framed for the Deck when this view IS a Deck, otherwise generic.
        AddCard(cards,
            id: onDeck ? "play-next-deck" : "installed-unplayed",
            title: onDeck ? "Play next on your Steam Deck" : "Installed but unplayed",
            subtitle: onDeck ? "Installed here, not started yet" : "On disk, you never started these",
            games.Where(g => g.Installed && Unplayed(g) && NotDone(g))
                 .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                 .Select(g => new RecommendationItem(g.AppId, g.Name,
                     onDeck ? "Installed on your Deck, not played yet." : "Installed but not started.")),
            perCard);

        // Paused — explicitly marked to return to.
        AddCard(cards, "paused", "Paused — worth returning to", "You marked these paused",
            games.Where(g => g.Status == GameStatus.Paused)
                 .OrderByDescending(g => g.PlaytimeMinutes ?? 0)
                 .Select(g => new RecommendationItem(g.AppId, g.Name, $"Paused with {Hours(g.PlaytimeMinutes)} played.")),
            perCard);

        // Recently added (since the baseline) and still untouched.
        AddCard(cards, "recently-added", "Recently added, still untouched", "New to your library",
            games.Where(g => g.AddedAgo is not null && Unplayed(g) && NotDone(g))
                 .Select(g => new RecommendationItem(g.AppId, g.Name, $"Added {g.AddedAgo}, not played yet.")),
            perCard);

        // Free up space — installed but you're done with them.
        AddCard(cards, "free-space", "Free up space", "Installed but you're done with them",
            games.Where(g => g.Installed && IsDone(g) && (g.Snapshot.SizeOnDiskBytes ?? 0) > 0)
                 .OrderByDescending(g => g.Snapshot.SizeOnDiskBytes ?? 0)
                 .Select(g => new RecommendationItem(g.AppId, g.Name, $"{g.Status} · {Size(g.Snapshot.SizeOnDiskBytes)} on disk.")),
            perCard);

        return cards;
    }

    private static void AddCard(List<RecommendationCard> cards, string id, string title, string subtitle,
        IEnumerable<RecommendationItem> items, int limit)
    {
        var list = items.Take(limit).ToList();
        if (list.Count > 0)
            cards.Add(new RecommendationCard(id, title, subtitle, list));
    }

    private static bool Unplayed(LibraryGame game) => (game.PlaytimeMinutes ?? 0) == 0 && game.Snapshot.LastPlayed is null;
    private static bool IsDone(LibraryGame game) => game.Status is { } status && DoneStatuses.Contains(status);
    private static bool NotDone(LibraryGame game) => !IsDone(game);

    private static string Hours(long? minutes) => minutes is { } m && m > 0 ? $"{m / 60.0:0.#} h" : "no time";

    private static string Size(long? bytes)
    {
        if (bytes is not { } b || b <= 0)
            return "unknown size";
        double gb = b / 1024d / 1024d / 1024d;
        return gb >= 1 ? $"{gb:0.#} GB" : $"{b / 1024d / 1024d:0} MB";
    }
}
