using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>
/// Merges a snapshot (raw facts) with a user profile (derived/user data) into one <see cref="LibraryView"/>.
/// The seam every consumer goes through, so user-data and facts stay queryable together. Rebuild per
/// read so the latest user-data writes are reflected.
/// </summary>
public static class LibraryViewBuilder
{
    public static LibraryView Build(SnapshotDocument snapshot, UserProfile? userData = null)
    {
        var games = snapshot.Games.Select(game =>
        {
            GameUserData? data = null;
            IReadOnlyList<Memory> memories = [];
            if (userData is not null)
            {
                userData.Games.TryGetValue(game.AppId, out data);
                memories = userData.Memories
                    .Where(m => m.Scope == MemoryScope.Game && m.Subject == game.AppId.ToString())
                    .ToList();
            }
            return new LibraryGame { Snapshot = game, UserData = data, Memories = memories };
        }).ToList();

        return new LibraryView
        {
            Games = games,
            Categories = snapshot.Categories,
            Libraries = snapshot.Libraries,
            CategoryDefinitions = userData?.CategoryDefinitions ?? new Dictionary<string, CategoryDefinition>(),
            GlobalMemories = userData?.Memories.Where(m => m.Scope == MemoryScope.Global).ToList() ?? [],
        };
    }
}
