using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Storage;

/// <summary>
/// Records when each owned game was first observed, so Shelfbound can infer "recently added/bought"
/// (Steam exposes no purchase date). On the first run it stores a baseline scan time and marks all
/// current apps as seen then; on later runs it timestamps any newly observed app. A scan whose
/// <c>scanScope</c> is broader than any prior scan reveals previously-owned games that are newly
/// <em>visible</em>, not newly <em>added</em>, so those get baselined rather than dated. Games first
/// seen after the baseline under a stable-or-narrower scope are the genuinely new ones.
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
