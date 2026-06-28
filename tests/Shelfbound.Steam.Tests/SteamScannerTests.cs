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
    public void Builds_snapshot_from_local_steam_files()
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
        File.WriteAllText(Path.Combine(_root, "config", "loginusers.vdf"), """
            "users" { "76561190000000000" { "AccountName" "tester" "PersonaName" "Tester" "MostRecent" "1" } }
            """);

        var device = new SnapshotDevice { Id = "test-id", Name = "TEST", Type = DeviceType.Desktop, Os = OsPlatform.Windows };
        var result = new SteamScanner().Scan(new SteamScanRequest
        {
            SteamRootPath = _root,
            Device = device,
            ToolVersion = "0.0.0-test",
        });

        var snapshot = result.Snapshot;
        var names = snapshot.Games.Select(g => g.Name).ToList();
        names.Count.ShouldBe(2);
        names.ShouldContain("Game Ten");
        names.ShouldContain("Game Twenty");
        snapshot.Stats.InstalledGameCount.ShouldBe(2);
        snapshot.Stats.TotalSizeOnDiskBytes.ShouldBe(3000);
        snapshot.SteamAccounts.ShouldHaveSingleItem().PersonaName.ShouldBe("Tester");
        snapshot.Libraries.ShouldHaveSingleItem().GameCount.ShouldBe(2);
        result.Warnings.ShouldBeEmpty();
    }
}
