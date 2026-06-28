using Shelfbound.Core.UserData;

namespace Shelfbound.Storage;

/// <summary>
/// Records when each owned game was first observed, so Shelfbound can infer "recently added/bought"
/// (Steam exposes no purchase date). On the first run it stores a baseline scan time and marks all
/// current apps as seen then; on later runs it timestamps any newly observed app. Games first seen
/// after the baseline are the genuinely new ones.
/// </summary>
public static class LibraryReconciler
{
    public static void RecordFirstSeen(IUserDataStore store, string ownerId, IEnumerable<int> appIds)
    {
        var ids = appIds.ToList();
        store.Update(ownerId, profile =>
        {
            var now = DateTimeOffset.UtcNow;
            profile.FirstScanAt ??= now;
            foreach (int appId in ids)
                profile.FirstSeen.TryAdd(appId, now);
            return 0;
        });
    }
}
