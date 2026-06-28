using Shouldly;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Query;

namespace Shelfbound.Steam.Tests;

public class LibraryQueryEngineTests
{
    private static SnapshotDocument Snapshot(params SnapshotGame[] games) => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        Source = new SnapshotSource { Tool = "t", ToolVersion = "0", Platform = OsPlatform.Windows },
        Device = new SnapshotDevice { Id = "d", Name = "d", Type = DeviceType.Desktop, Os = OsPlatform.Windows },
        Games = games,
        Stats = new SnapshotStats { LibraryCount = 0, InstalledGameCount = games.Count(g => g.Installed), TotalSizeOnDiskBytes = 0 },
    };

    private static SnapshotGame Game(int id, string name, bool installed, long? playtime = null, params string[] categories) =>
        new() { AppId = id, Name = name, Installed = installed, PlaytimeMinutes = playtime, Categories = categories };

    [Fact]
    public void Filters_by_text_installed_and_categories()
    {
        var snap = Snapshot(
            Game(1, "Outer Wilds", installed: true, 120, "Deck", "Directly Choice"),
            Game(2, "Hades", installed: true, 0, "Deck"),
            Game(3, "Stardew Valley", installed: false, 3000, "Hold"));

        LibraryQueryEngine.Search(snap, new LibraryFilter { Text = "out" })
            .ShouldHaveSingleItem().Name.ShouldBe("Outer Wilds");

        LibraryQueryEngine.Search(snap, new LibraryFilter { Installed = false })
            .ShouldHaveSingleItem().Name.ShouldBe("Stardew Valley");

        LibraryQueryEngine.Search(snap, new LibraryFilter { CategoriesAny = ["deck"] }) // case-insensitive
            .Select(g => g.AppId).ShouldBe([1, 2], ignoreOrder: true);

        LibraryQueryEngine.Search(snap, new LibraryFilter { CategoriesNone = ["Deck"] })
            .ShouldHaveSingleItem().Name.ShouldBe("Stardew Valley");
    }

    [Fact]
    public void Filters_by_playtime_and_sorts_and_limits()
    {
        var snap = Snapshot(
            Game(1, "A", true, 50),
            Game(2, "B", true, 500),
            Game(3, "C", true, 0));

        LibraryQueryEngine.Search(snap, new LibraryFilter { MaxPlaytimeMinutes = 100 })
            .Select(g => g.Name).ShouldBe(["A", "C"], ignoreOrder: true);

        LibraryQueryEngine.Search(snap, new LibraryFilter { Sort = LibrarySort.PlaytimeMinutes, Descending = true, Limit = 1 })
            .ShouldHaveSingleItem().Name.ShouldBe("B");
    }

    [Fact]
    public void Summarize_reports_totals_and_playtime()
    {
        var snap = Snapshot(
            Game(1, "A", true, 50, "Deck"),
            Game(2, "B", false, 500));

        var summary = LibraryQueryEngine.Summarize(snap);
        summary.TotalGames.ShouldBe(2);
        summary.InstalledGames.ShouldBe(1);
        summary.CategorizedGames.ShouldBe(1);
        summary.TotalPlaytimeMinutes.ShouldBe(550);
    }

    [Fact]
    public void Summarize_playtime_is_null_when_not_enriched()
    {
        LibraryQueryEngine.Summarize(Snapshot(Game(1, "A", true))).TotalPlaytimeMinutes.ShouldBeNull();
    }
}
