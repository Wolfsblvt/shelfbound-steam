using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;
using Shouldly;

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
        Stats = new SnapshotStats
        {
            LibraryCount = 0,
            InstalledGameCount = 0,
            TotalSizeOnDiskBytes = 0,
            Scope = LibraryScope.FullLibrary,
        },
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

    // End-to-end: a scope expansion (installedOnly baseline → fullLibrary scan) must NOT make
    // previously-owned games read as "recently added"; a genuine purchase once scope is stable still does.
    [Fact]
    public void Scope_expansion_does_not_produce_false_recently_added()
    {
        var profile = new UserProfile { OwnerId = "x" };

        // Baseline: an installed-only scan sees only the installed games (1, 2).
        var baseline = DateTimeOffset.UtcNow.AddDays(-30);
        UserDataActions.RecordFirstSeen(profile, [1, 2], baseline, LibraryScope.InstalledOnly);

        // Later: a full-library scan reveals owned-but-not-installed games (3, 4) for the first time.
        var fullScan = DateTimeOffset.UtcNow.AddDays(-10);
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3, 4], fullScan, LibraryScope.FullLibrary);

        var view = LibraryViewBuilder.Build(
            Snapshot(
                new SnapshotGame { AppId = 1, Name = "Installed A", Installed = true },
                new SnapshotGame { AppId = 2, Name = "Installed B", Installed = true },
                new SnapshotGame { AppId = 3, Name = "Owned C", Installed = false },
                new SnapshotGame { AppId = 4, Name = "Owned D", Installed = false }),
            profile);

        // None read as "recently added" — the wider scan made them visible, not newly acquired.
        view.Games.ShouldAllBe(g => g.AddedAgo == null);

        // A genuine purchase after the scope has stabilized at full DOES read as recently added.
        var purchase = DateTimeOffset.UtcNow.AddDays(-3);
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3, 4, 5], purchase, LibraryScope.FullLibrary);

        var afterPurchase = LibraryViewBuilder.Build(
            Snapshot(new SnapshotGame { AppId = 5, Name = "Just Bought", Installed = true }),
            profile);
        afterPurchase.Games.Single(g => g.AppId == 5).AddedAgo.ShouldBe("3 days ago");
    }

    [Fact]
    public void Last_played_ago_is_never_when_unknown()
    {
        var view = LibraryViewBuilder.Build(Snapshot(new SnapshotGame { AppId = 1, Name = "G", Installed = true }));
        view.Games[0].LastPlayedAgo.ShouldBe("never");
    }

    [Fact]
    public void Partial_view_never_surfaces_an_acquisition_claim_from_a_stored_timestamp()
    {
        var baseline = DateTimeOffset.UtcNow.AddDays(-30);
        var profile = new UserProfile { OwnerId = "x", FirstScanAt = baseline };
        profile.FirstSeen[1] = DateTimeOffset.UtcNow.AddDays(-3);
        SnapshotDocument partial = Snapshot(new SnapshotGame { AppId = 1, Name = "Observed", Installed = false }) with
        {
            Stats = new SnapshotStats
            {
                LibraryCount = 0,
                InstalledGameCount = 0,
                TotalSizeOnDiskBytes = 0,
                Scope = LibraryScope.ObservedSubset,
            },
        };

        LibraryViewBuilder.Build(partial, profile).Games.Single().AddedAgo.ShouldBeNull();

        SnapshotDocument complete = partial with
        {
            Stats = partial.Stats with { Scope = LibraryScope.FullLibrary },
        };
        LibraryViewBuilder.Build(complete, profile).Games.Single().AddedAgo.ShouldBe("3 days ago");
    }

    [Fact]
    public void Legacy_false_full_library_scope_is_partial_in_the_read_view()
    {
        var baseline = DateTimeOffset.UtcNow.AddDays(-30);
        var profile = new UserProfile { OwnerId = "x", FirstScanAt = baseline };
        profile.FirstSeen[1] = DateTimeOffset.UtcNow.AddDays(-3);
        SnapshotDocument legacy = Snapshot(new SnapshotGame { AppId = 1, Name = "Legacy", Installed = false }) with
        {
            SchemaVersion = "0.5.0",
        };

        LibraryView view = LibraryViewBuilder.Build(legacy, profile);

        view.Scope.ShouldBe(LibraryScope.ObservedSubset);
        view.Games.Single().AddedAgo.ShouldBeNull();
        legacy.Stats.Scope.ShouldBe(LibraryScope.FullLibrary);
    }
}
