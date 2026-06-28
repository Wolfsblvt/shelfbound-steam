using Shouldly;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;

namespace Shelfbound.Steam.Tests;

public class RecencyTests
{
    private static SnapshotDocument Snapshot(params SnapshotGame[] games) => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "t",
        CreatedAt = DateTimeOffset.UtcNow,
        Source = new SnapshotSource { Tool = "t", ToolVersion = "0", Platform = OsPlatform.Windows },
        Device = new SnapshotDevice { Id = "d", Name = "d", Type = DeviceType.Desktop, Os = OsPlatform.Windows },
        Games = games,
        Stats = new SnapshotStats { LibraryCount = 0, InstalledGameCount = 0, TotalSizeOnDiskBytes = 0 },
    };

    [Fact]
    public void Added_ago_only_for_games_first_seen_after_the_baseline_scan()
    {
        var baseline = DateTimeOffset.UtcNow.AddDays(-30);
        var profile = new UserProfile { OwnerId = "x", FirstScanAt = baseline };
        profile.FirstSeen[1] = baseline;                          // present at baseline
        profile.FirstSeen[2] = DateTimeOffset.UtcNow.AddDays(-3); // added later

        var view = LibraryViewBuilder.Build(
            Snapshot(
                new SnapshotGame { AppId = 1, Name = "Old", Installed = true },
                new SnapshotGame { AppId = 2, Name = "New", Installed = true }),
            profile);

        view.Games.Single(g => g.AppId == 1).AddedAgo.ShouldBeNull();
        view.Games.Single(g => g.AppId == 2).AddedAgo.ShouldBe("3 days ago");
    }

    [Fact]
    public void Last_played_ago_is_never_when_unknown()
    {
        var view = LibraryViewBuilder.Build(Snapshot(new SnapshotGame { AppId = 1, Name = "G", Installed = true }));
        view.Games[0].LastPlayedAgo.ShouldBe("never");
    }
}
