using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;
using Shouldly;

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

    private static LibraryView View(params SnapshotGame[] games) => LibraryViewBuilder.Build(Snapshot(games));
    private static LibraryView View(UserProfile userData, params SnapshotGame[] games) => LibraryViewBuilder.Build(Snapshot(games), userData);

    [Fact]
    public void Filters_by_text_installed_and_categories()
    {
        var view = View(
            Game(1, "Outer Wilds", installed: true, 120, "Deck", "Directly Choice"),
            Game(2, "Hades", installed: true, 0, "Deck"),
            Game(3, "Stardew Valley", installed: false, 3000, "Hold"));

        LibraryQueryEngine.Search(view, new LibraryFilter { Text = "out" })
            .ShouldHaveSingleItem().Name.ShouldBe("Outer Wilds");
        LibraryQueryEngine.Search(view, new LibraryFilter { Installed = false })
            .ShouldHaveSingleItem().Name.ShouldBe("Stardew Valley");
        LibraryQueryEngine.Search(view, new LibraryFilter { CategoriesAny = ["deck"] }) // case-insensitive
            .Select(g => g.AppId).ShouldBe([1, 2], ignoreOrder: true);
        LibraryQueryEngine.Search(view, new LibraryFilter { CategoriesNone = ["Deck"] })
            .ShouldHaveSingleItem().Name.ShouldBe("Stardew Valley");
    }

    [Fact]
    public void Filters_by_playtime_and_sorts_and_limits()
    {
        var view = View(Game(1, "A", true, 50), Game(2, "B", true, 500), Game(3, "C", true, 0));

        LibraryQueryEngine.Search(view, new LibraryFilter { MaxPlaytimeMinutes = 100 })
            .Select(g => g.Name).ShouldBe(["A", "C"], ignoreOrder: true);
        LibraryQueryEngine.Search(view, new LibraryFilter { Sort = LibrarySort.PlaytimeMinutes, Descending = true, Limit = 1 })
            .ShouldHaveSingleItem().Name.ShouldBe("B");
    }

    [Fact]
    public void Filters_by_user_data_status_rating_and_completion()
    {
        var profile = new UserProfile { OwnerId = "x" };
        UserDataActions.UpsertGame(profile, 1, g => g with { Status = GameStatus.Finished, Rating = GameRating.Loved, CompletionPercent = 100 });
        UserDataActions.UpsertGame(profile, 2, g => g with { Status = GameStatus.Playing });

        var view = View(profile, Game(1, "Done", true), Game(2, "Now", true), Game(3, "Untouched", true));

        LibraryQueryEngine.Search(view, new LibraryFilter { Status = GameStatus.Finished })
            .ShouldHaveSingleItem().Name.ShouldBe("Done");
        LibraryQueryEngine.Search(view, new LibraryFilter { Rating = GameRating.Loved })
            .ShouldHaveSingleItem().Name.ShouldBe("Done");
        LibraryQueryEngine.Search(view, new LibraryFilter { MinCompletionPercent = 50 })
            .ShouldHaveSingleItem().Name.ShouldBe("Done");
    }

    [Fact]
    public void Summarize_reports_totals_and_playtime()
    {
        var summary = LibraryQueryEngine.Summarize(View(Game(1, "A", true, 50, "Deck"), Game(2, "B", false, 500)));
        summary.TotalGames.ShouldBe(2);
        summary.InstalledGames.ShouldBe(1);
        summary.CategorizedGames.ShouldBe(1);
        summary.TotalPlaytimeMinutes.ShouldBe(550);
    }

    [Fact]
    public void Summarize_playtime_is_null_when_not_enriched()
    {
        LibraryQueryEngine.Summarize(View(Game(1, "A", true))).TotalPlaytimeMinutes.ShouldBeNull();
    }

    [Fact]
    public void Summarize_carries_library_scope()
    {
        // Default snapshot is installed-only; the scope must reach the summary so the AI doesn't read
        // "not found" as "not owned".
        LibraryQueryEngine.Summarize(View(Game(1, "A", true))).Scope.ShouldBe(LibraryScope.InstalledOnly);

        SnapshotDocument full = Snapshot(Game(1, "A", true)) with
        {
            Stats = new SnapshotStats { LibraryCount = 0, InstalledGameCount = 1, TotalSizeOnDiskBytes = 0, Scope = LibraryScope.FullLibrary },
        };
        LibraryQueryEngine.Summarize(LibraryViewBuilder.Build(full)).Scope.ShouldBe(LibraryScope.FullLibrary);
    }
}
