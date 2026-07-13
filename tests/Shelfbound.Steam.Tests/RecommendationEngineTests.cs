using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class RecommendationEngineTests
{
    private static SnapshotDocument Snapshot(DeviceType deviceType, params SnapshotGame[] games) => new()
    {
        SchemaVersion = "0.3.0",
        SnapshotId = "t",
        CreatedAt = DateTimeOffset.UtcNow,
        Source = new SnapshotSource { Tool = "t", ToolVersion = "0", Platform = OsPlatform.Windows },
        Device = new SnapshotDevice { Id = "d", Name = "d", Type = deviceType, Os = OsPlatform.Linux },
        Games = games,
        Stats = new SnapshotStats { LibraryCount = 0, InstalledGameCount = 0, TotalSizeOnDiskBytes = 0 },
    };

    private static SnapshotGame Game(int appId, string name, bool installed, long? playtime = null, long? size = null) =>
        new() { AppId = appId, Name = name, Installed = installed, PlaytimeMinutes = playtime, SizeOnDiskBytes = size };

    [Fact]
    public void Deck_device_frames_unplayed_installed_as_play_next_on_deck()
    {
        var view = LibraryViewBuilder.Build(Snapshot(DeviceType.SteamDeck, Game(1, "Hades", installed: true)));

        var cards = RecommendationEngine.Build(view);

        cards.ShouldContain(c => c.Id == "play-next-deck");
        cards.First(c => c.Id == "play-next-deck").Items.ShouldContain(i => i.AppId == 1);
    }

    [Fact]
    public void Non_deck_device_never_suggests_the_deck()
    {
        var view = LibraryViewBuilder.Build(Snapshot(DeviceType.Desktop, Game(1, "Hades", installed: true)));

        var cards = RecommendationEngine.Build(view);

        cards.ShouldNotContain(c => c.Id == "play-next-deck");
        cards.ShouldContain(c => c.Id == "installed-unplayed");
    }

    [Fact]
    public void Free_space_lists_finished_installed_games()
    {
        var profile = new UserProfile { OwnerId = "x" };
        UserDataActions.UpsertGame(profile, 1, g => g with { Status = GameStatus.Finished });

        var view = LibraryViewBuilder.Build(
            Snapshot(DeviceType.Desktop, Game(1, "Witcher 3", installed: true, playtime: 6000, size: 50_000_000_000)),
            profile);

        var cards = RecommendationEngine.Build(view);

        cards.ShouldContain(c => c.Id == "free-space" && c.Items.Any(i => i.AppId == 1));
    }
}
