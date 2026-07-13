using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class ProfileQueryTests
{
    private static SnapshotDocument Snapshot(IReadOnlyList<SnapshotCategory> categories, params SnapshotGame[] games) => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "t",
        CreatedAt = DateTimeOffset.UtcNow,
        Source = new SnapshotSource { Tool = "t", ToolVersion = "0", Platform = OsPlatform.Windows },
        Device = new SnapshotDevice { Id = "d", Name = "d", Type = DeviceType.Desktop, Os = OsPlatform.Windows },
        Games = games,
        Categories = categories,
        Stats = new SnapshotStats { LibraryCount = 0, InstalledGameCount = 0, TotalSizeOnDiskBytes = 0 },
    };

    [Fact]
    public void Empty_profile_is_not_set_up_and_suggests_most_played_unrated_games()
    {
        var snapshot = Snapshot(
            [new SnapshotCategory { Name = "Deck", GameCount = 1 }],
            new SnapshotGame { AppId = 1, Name = "Played", Installed = true, PlaytimeMinutes = 600 },
            new SnapshotGame { AppId = 2, Name = "Barely", Installed = true, PlaytimeMinutes = 5 });

        var summary = ProfileQuery.Summarize(LibraryViewBuilder.Build(snapshot, userData: null));

        summary.IsSetUp.ShouldBeFalse();
        summary.RatedGames.ShouldBe(0);
        summary.UndefinedCategories.ShouldBe(["Deck"]);
        summary.SuggestedGamesToRate[0].Name.ShouldBe("Played"); // highest playtime first
    }

    [Fact]
    public void Set_up_profile_reflects_ratings_and_meanings_and_excludes_rated_from_suggestions()
    {
        var snapshot = Snapshot(
            [new SnapshotCategory { Name = "Deck", GameCount = 1 }],
            new SnapshotGame { AppId = 1, Name = "Loved", Installed = true, PlaytimeMinutes = 600 },
            new SnapshotGame { AppId = 2, Name = "Unrated", Installed = true, PlaytimeMinutes = 5 });

        var profile = new UserProfile { OwnerId = "x" };
        UserDataActions.UpsertGame(profile, 1, g => g with { Rating = GameRating.Loved });
        UserDataActions.SetCategoryDefinition(profile, "Deck", "portable games");

        var summary = ProfileQuery.Summarize(LibraryViewBuilder.Build(snapshot, profile));

        summary.IsSetUp.ShouldBeTrue();
        summary.RatedGames.ShouldBe(1);
        summary.CategoryMeanings.ShouldBe(1);
        summary.UndefinedCategories.ShouldBeEmpty();
        summary.SuggestedGamesToRate.ShouldHaveSingleItem().Name.ShouldBe("Unrated");
    }
}
