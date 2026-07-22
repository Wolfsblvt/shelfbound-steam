using Shelfbound.Client;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Tray;
using Shouldly;

namespace Shelfbound.Tray.Tests;

public sealed class PrivateGameExclusionTests
{
    [Fact]
    public void Settings_default_off_and_round_trip_only_valid_local_overrides()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "tray.json");

        AppSettings.Load(path).ExcludeSteamPrivateGames.ShouldBeFalse();

        var settings = new AppSettings
        {
            ExcludeSteamPrivateGames = true,
            PrivateGameUnskipAppIds = [40, -1, 40, 20],
        };
        settings.Save(path);

        AppSettings restored = AppSettings.Load(path);
        restored.ExcludeSteamPrivateGames.ShouldBeTrue();
        restored.PrivateGameUnskipAppIds.ShouldBe([20, 40]);

        File.WriteAllText(path, "{ not-json");
        AppSettings corrupt = AppSettings.Load(path);
        corrupt.ExcludeSteamPrivateGames.ShouldBeFalse();
        corrupt.PrivateGameUnskipAppIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preview_unskip_and_confirm_reuse_the_same_scan_evidence_and_exact_bytes()
    {
        var uploadedBodies = new List<string>();
        int buildCount = 0;
        int preparationCount = 0;
        var settings = new AppSettings
        {
            DeviceName = "Fixture device",
            DeviceType = DeviceType.Desktop,
            ExcludeSteamPrivateGames = true,
        };
        using var agent = CreateAgent(
            settings,
            uploadedBodies,
            () => buildCount++,
            () => preparationCount++);

        PreparedSync initial = (await agent.PrepareSyncAsync()).ShouldNotBeNull();
        PreparedSync overridden = agent.UnskipPrivateGame(initial, 20);
        await agent.SyncNowAsync(overridden);

        buildCount.ShouldBe(1, "un-skip regenerates from the frozen scan rather than rescanning");
        preparationCount.ShouldBe(1, "positive cache evidence is read once for the preparation");
        initial.SkippedGames.ShouldHaveSingleItem().Name.ShouldBe("Skipped Beta");
        initial.Upload.Snapshot.Games.Select(game => game.AppId).ShouldBe([10]);
        overridden.SkippedGames.ShouldBeEmpty();
        overridden.Upload.Snapshot.Games.Select(game => game.AppId).ShouldBe([10, 20]);
        settings.PrivateGameUnskipAppIds.ShouldBe([20]);
        uploadedBodies.ShouldBe([overridden.Upload.Json]);
        agent.History.ShouldNotContain(entry => entry.Contains("Skipped Beta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Background_sync_uses_the_identical_preparation_primitive_and_saved_overrides()
    {
        var uploadedBodies = new List<string>();
        var settings = new AppSettings
        {
            DeviceName = "Fixture device",
            DeviceType = DeviceType.Desktop,
            AutoSync = true,
            HostedUploadConsentVersion = HostedProjection.ProjectionVersion,
            ExcludeSteamPrivateGames = true,
            PrivateGameUnskipAppIds = [20],
            IntervalMinutes = 60,
        };
        using var agent = CreateAgent(settings, uploadedBodies, () => { }, () => { });

        PreparedSync manual = (await agent.PrepareSyncAsync()).ShouldNotBeNull();
        agent.Start();
        await WaitUntilAsync(() => uploadedBodies.Count == 1);

        uploadedBodies.ShouldBe([manual.Upload.Json]);
        manual.SkippedGames.ShouldBeEmpty();
    }

    private static SyncAgent CreateAgent(
        AppSettings settings,
        List<string> uploadedBodies,
        Action onBuild,
        Action onPrepare) =>
        new(settings, "upload-token", new SyncAgentDependencies
        {
            BuildSnapshotAsync = (_, _) =>
            {
                onBuild();
                SnapshotDocument snapshot = Snapshot();
                return Task.FromResult(new SnapshotBuildResult(snapshot, snapshot.Device, []));
            },
            PreparePrivateGameUpload = (snapshot, enabled, overrides) =>
            {
                onPrepare();
                IReadOnlySet<int> evidence = enabled
                    ? new HashSet<int> { 20 }
                    : new HashSet<int>();
                return enabled
                    ? PrivateGameUploadPreparer.PrepareFromEvidence(snapshot, evidence, overrides)
                    : PrivateGameUploadPreparer.Prepare(
                        snapshot,
                        enabled: false,
                        overrides,
                        steamPath: null,
                        machineName: "synthetic-host");
            },
            ConnectAsync = (_, _, _, _) => Task.FromResult<ConnectFlowResult?>(null),
            UploadAsync = (_, _, upload, _) =>
            {
                uploadedBodies.Add(upload.Json);
                return Task.FromResult(new UploadResult
                {
                    Status = UploadStatus.Success,
                    ErrorCode = UploadErrorCode.None,
                    GameCount = upload.GameCount,
                });
            },
            LoadToken = () => "upload-token",
            ClearToken = () => { },
            SaveSettings = _ => { },
            ApplyAutoStart = _ => { },
        });

    private static SnapshotDocument Snapshot() => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "55555555-5555-5555-5555-555555555555",
        CreatedAt = DateTimeOffset.Parse("2026-07-22T01:02:03+00:00"),
        Source = new SnapshotSource { Tool = "test", ToolVersion = "1", Platform = OsPlatform.Windows },
        Device = new SnapshotDevice
        {
            Id = "66666666-6666-6666-6666-666666666666",
            Name = "Fixture device",
            Type = DeviceType.Desktop,
            Os = OsPlatform.Windows,
        },
        Libraries = [new SnapshotLibrary { Index = 0, Label = "Primary", GameCount = 2 }],
        Games =
        [
            new SnapshotGame
            {
                AppId = 10,
                Name = "Retained Alpha",
                Installed = true,
                LibraryIndex = 0,
                SizeOnDiskBytes = 100,
                Categories = ["Shared"],
            },
            new SnapshotGame
            {
                AppId = 20,
                Name = "Skipped Beta",
                Installed = true,
                LibraryIndex = 0,
                SizeOnDiskBytes = 200,
                Categories = ["Shared"],
            },
        ],
        Categories = [new SnapshotCategory { Name = "Shared", GameCount = 2 }],
        Stats = new SnapshotStats
        {
            LibraryCount = 1,
            InstalledGameCount = 2,
            TotalSizeOnDiskBytes = 300,
            Scope = LibraryScope.FullLibrary,
        },
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        condition().ShouldBeTrue("background sync should complete within the test deadline");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"shelfbound-tray-private-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* test cleanup only */ }
        }
    }
}
