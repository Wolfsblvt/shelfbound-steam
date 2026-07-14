using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Storage;

/// <summary>
/// Records conservative first-observation state. The first run establishes a baseline; later apps are
/// dated only under stable complete coverage. Broader or partial scans baseline new appids because
/// presence without complete prior absence cannot support an acquisition claim.
/// </summary>
public static class LibraryReconciler
{
    public static void RecordFirstSeen(IUserDataStore store, string ownerId, IEnumerable<int> appIds, LibraryScope scanScope)
    {
        var ids = appIds.ToList();
        store.Update(ownerId, profile =>
        {
            UserDataActions.RecordFirstSeen(profile, ids, DateTimeOffset.UtcNow, scanScope);
            return 0;
        });
    }
}
