using Shouldly;
using Shelfbound.Core.UserData;

namespace Shelfbound.Steam.Tests;

public class UserDataActionsTests
{
    [Fact]
    public void Record_first_seen_sets_baseline_once_and_only_timestamps_new_apps()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var firstScan = DateTimeOffset.UtcNow.AddDays(-10);

        UserDataActions.RecordFirstSeen(profile, [1, 2], firstScan);

        // First call establishes the baseline and marks all current apps as seen then.
        profile.FirstScanAt.ShouldBe(firstScan);
        profile.FirstSeen[1].ShouldBe(firstScan);
        profile.FirstSeen[2].ShouldBe(firstScan);

        var laterScan = DateTimeOffset.UtcNow;
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3], laterScan);

        // Baseline is sticky; existing apps keep their original timestamp; only app 3 is new.
        profile.FirstScanAt.ShouldBe(firstScan);
        profile.FirstSeen[1].ShouldBe(firstScan);
        profile.FirstSeen[3].ShouldBe(laterScan);
    }
}
