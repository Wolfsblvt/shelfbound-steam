using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Enrichment;
using Shelfbound.Steam.Web;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SteamWebEnricherTests
{
    [Fact]
    public void Sets_playtime_on_installed_and_adds_owned_not_installed_with_categories()
    {
        var snapshot = new SnapshotDocument
        {
            SchemaVersion = SnapshotSchema.Version,
            SnapshotId = "t",
            CreatedAt = DateTimeOffset.UtcNow,
            Source = new SnapshotSource { Tool = "t", ToolVersion = "0", Platform = OsPlatform.Windows },
            Device = new SnapshotDevice { Id = "d", Name = "d", Type = DeviceType.Desktop, Os = OsPlatform.Windows },
            Games = [new SnapshotGame { AppId = 10, Name = "Installed Game", Installed = true, LibraryIndex = 0 }],
            Stats = new SnapshotStats { LibraryCount = 1, InstalledGameCount = 1, TotalSizeOnDiskBytes = 0 },
        };

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
        // Enrichment adds owned-but-not-installed games, so the snapshot now covers the full library.
        enriched.Stats.Scope.ShouldBe(LibraryScope.FullLibrary);

        var installed = enriched.Games.Single(g => g.AppId == 10);
        installed.Installed.ShouldBeTrue();
        installed.PlaytimeMinutes.ShouldBe(120);

        var notInstalled = enriched.Games.Single(g => g.AppId == 20);
        notInstalled.Installed.ShouldBeFalse();
        notInstalled.LibraryIndex.ShouldBeNull();
        notInstalled.PlaytimeMinutes.ShouldBe(30);
        notInstalled.Categories.ShouldBe(["Backlog"]);
    }
}
