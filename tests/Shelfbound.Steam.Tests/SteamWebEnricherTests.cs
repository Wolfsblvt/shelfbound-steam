using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Enrichment;
using Shelfbound.Steam.Web;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SteamWebEnricherTests
{
    private static SnapshotDocument Snapshot(params SnapshotGame[] games) => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "t",
        CreatedAt = DateTimeOffset.Parse("2026-07-14T12:00:00+00:00"),
        Source = new SnapshotSource { Tool = "t", ToolVersion = "0", Platform = OsPlatform.Windows },
        Device = new SnapshotDevice { Id = "d", Name = "d", Type = DeviceType.Desktop, Os = OsPlatform.Windows },
        Games = games,
        Stats = new SnapshotStats { LibraryCount = 1, InstalledGameCount = 1, TotalSizeOnDiskBytes = 0 },
    };

    [Fact]
    public void Adds_positive_observations_without_claiming_a_complete_library()
    {
        SnapshotDocument snapshot = Snapshot(
            new SnapshotGame { AppId = 10, Name = "Installed Game", Installed = true, LibraryIndex = 0 });

        var categoriesByApp = new Dictionary<int, IReadOnlyList<string>> { [20] = ["Backlog"] };
        var owned = new List<OwnedGame>
        {
            new() { AppId = 10, Name = "Installed Game", PlaytimeForeverMinutes = 120 },
            new() { AppId = 20, Name = "Owned Not Installed", PlaytimeForeverMinutes = 30 },
        };

        // A scan is installed-only until enriched.
        snapshot.Stats.Scope.ShouldBe(LibraryScope.InstalledOnly);

        var enriched = SteamWebEnricher.Enrich(snapshot, categoriesByApp, owned);

        enriched.Games.Count.ShouldBe(2);
        enriched.Stats.Scope.ShouldBe(LibraryScope.ObservedSubset);

        var installed = enriched.Games.Single(g => g.AppId == 10);
        installed.Installed.ShouldBeTrue();
        installed.PlaytimeMinutes.ShouldBe(120);

        var notInstalled = enriched.Games.Single(g => g.AppId == 20);
        notInstalled.Installed.ShouldBeFalse();
        notInstalled.LibraryIndex.ShouldBeNull();
        notInstalled.PlaytimeMinutes.ShouldBe(30);
        notInstalled.Categories.ShouldBe(["Backlog"]);
    }

    [Fact]
    public void Empty_observations_leave_the_installed_only_snapshot_unchanged()
    {
        SnapshotDocument snapshot = Snapshot(
            new SnapshotGame { AppId = 10, Name = "Installed Game", Installed = true, LibraryIndex = 0 });

        SnapshotDocument enriched = SteamWebEnricher.Enrich(
            snapshot,
            new Dictionary<int, IReadOnlyList<string>>(),
            []);

        enriched.ShouldBeSameAs(snapshot);
        enriched.Stats.Scope.ShouldBe(LibraryScope.InstalledOnly);
    }

    [Fact]
    public void Duplicate_appids_merge_to_one_deterministic_row_with_strongest_observations()
    {
        DateTimeOffset older = DateTimeOffset.Parse("2026-07-01T10:00:00+00:00");
        DateTimeOffset newer = DateTimeOffset.Parse("2026-07-10T10:00:00+00:00");
        SnapshotDocument snapshot = Snapshot(
            new SnapshotGame
            {
                AppId = 10,
                Name = "Installed Game",
                Installed = true,
                LibraryIndex = 1,
                InstallDir = "z-folder",
                Categories = ["Zeta", "Action"],
            },
            new SnapshotGame
            {
                AppId = 10,
                Name = "Installed Game",
                Installed = true,
                LibraryIndex = 0,
                InstallDir = "a-folder",
                Categories = ["Alpha", "action"],
            });
        var observations = new List<OwnedGame>
        {
            new() { AppId = 10, Name = "Installed Game", PlaytimeForeverMinutes = 20, LastPlayed = newer },
            new() { AppId = 10, Name = "Installed Game", PlaytimeForeverMinutes = 90, LastPlayed = older },
            new() { AppId = 20, Name = "Zed", PlaytimeForeverMinutes = 30, LastPlayed = older },
            new() { AppId = 20, Name = "Alpha", PlaytimeForeverMinutes = 10, LastPlayed = newer },
            new() { AppId = 20, Name = "App 20", PlaytimeForeverMinutes = 0 },
        };

        SnapshotDocument forward = SteamWebEnricher.Enrich(
            snapshot,
            new Dictionary<int, IReadOnlyList<string>> { [20] = ["Backlog"] },
            observations);
        SnapshotDocument reverse = SteamWebEnricher.Enrich(
            snapshot with { Games = snapshot.Games.Reverse().ToArray() },
            new Dictionary<int, IReadOnlyList<string>> { [20] = ["Backlog"] },
            observations.AsEnumerable().Reverse().ToArray());

        SnapshotSerializer.Serialize(reverse, indented: false)
            .ShouldBe(SnapshotSerializer.Serialize(forward, indented: false));
        forward.Games.Select(game => game.AppId).ShouldBe([10, 20]);
        forward.Games.Select(game => game.AppId).Distinct().Count().ShouldBe(forward.Games.Count);

        SnapshotGame installed = forward.Games.Single(game => game.AppId == 10);
        installed.LibraryIndex.ShouldBe(0);
        installed.InstallDir.ShouldBe("a-folder");
        installed.PlaytimeMinutes.ShouldBe(90);
        installed.LastPlayed.ShouldBe(newer);
        installed.Categories.ShouldBe(["Action", "Alpha", "Zeta"]);

        SnapshotGame observed = forward.Games.Single(game => game.AppId == 20);
        observed.Name.ShouldBe("Alpha");
        observed.PlaytimeMinutes.ShouldBe(30);
        observed.LastPlayed.ShouldBe(newer);
    }
}
