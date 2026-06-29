using Shouldly;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Steam.Tests;

public sealed class SteamScannerTests : IDisposable
{
    private readonly string _root;

    public SteamScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shelfbound-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void Builds_snapshot_with_games_accounts_and_categories()
    {
        string steamApps = Path.Combine(_root, "steamapps");
        string escapedRoot = _root.Replace("\\", "\\\\");

        File.WriteAllText(Path.Combine(steamApps, "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path" "{{escapedRoot}}"
                    "label" ""
                    "apps" { "10" "1000" "20" "2000" }
                }
            }
            """);

        File.WriteAllText(Path.Combine(steamApps, "appmanifest_10.acf"), """
            "AppState" { "appid" "10" "name" "Game Ten" "StateFlags" "4" "SizeOnDisk" "1000" }
            """);
        File.WriteAllText(Path.Combine(steamApps, "appmanifest_20.acf"), """
            "AppState" { "appid" "20" "name" "Game Twenty" "StateFlags" "4" "SizeOnDisk" "2000" }
            """);

        // SteamID64 76561197960265738 maps to account id 10 (userdata/10).
        File.WriteAllText(Path.Combine(_root, "config", "loginusers.vdf"), """
            "users" { "76561197960265738" { "AccountName" "tester" "PersonaName" "Tester" "MostRecent" "1" } }
            """);

        string remote = Path.Combine(_root, "userdata", "10", "7", "remote");
        Directory.CreateDirectory(remote);
        File.WriteAllText(Path.Combine(remote, "sharedconfig.vdf"), """
            "UserRoamingConfigStore" { "Software" { "Valve" { "Steam" { "apps"
            {
                "10" { "tags" { "0" "Next" "1" "Deck" } }
                "20" { "tags" { "0" "Finished" } }
            } } } } }
            """);

        var device = new SnapshotDevice { Id = "test-id", Name = "TEST", Type = DeviceType.Desktop, Os = OsPlatform.Windows };
        var result = new SteamScanner().Scan(new SteamScanRequest
        {
            SteamRootPath = _root,
            Device = device,
            ToolVersion = "0.0.0-test",
        });

        var snapshot = result.Snapshot;
        snapshot.Games.Count.ShouldBe(2);
        snapshot.Stats.InstalledGameCount.ShouldBe(2);
        snapshot.Stats.TotalSizeOnDiskBytes.ShouldBe(3000);
        // A local scan never reaches the Steam Web API, so it's installed-only.
        snapshot.Stats.Scope.ShouldBe(LibraryScope.InstalledOnly);
        snapshot.SteamAccounts.ShouldHaveSingleItem().PersonaName.ShouldBe("Tester");
        snapshot.Libraries.ShouldHaveSingleItem().GameCount.ShouldBe(2);

        // Categories attached to games (tag order preserved) and summarized on the document.
        snapshot.Games.Single(g => g.AppId == 10).Categories.ShouldBe(["Next", "Deck"]);
        snapshot.Games.Single(g => g.AppId == 20).Categories.ShouldBe(["Finished"]);
        snapshot.Categories.Select(c => c.Name).ShouldBe(["Deck", "Finished", "Next"]);
        snapshot.Categories.Single(c => c.Name == "Deck").GameCount.ShouldBe(1);

        result.Warnings.ShouldBeEmpty();
    }
}
