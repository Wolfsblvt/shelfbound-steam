using Shelfbound.Client;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Tray;
using Shouldly;

namespace Shelfbound.Tray.Tests;

public sealed class DeviceTypeSetupTests
{
    [Fact]
    public void Missing_choice_is_incomplete_but_explicit_other_is_complete()
    {
        DeviceTypeSetup.IsComplete(null).ShouldBeFalse();
        DeviceTypeSetup.IsComplete(DeviceType.Unknown).ShouldBeTrue();
        DeviceTypeSetup.IsComplete(DeviceType.Server).ShouldBeFalse();
    }

    [Fact]
    public void Tolerant_settings_loading_preserves_unrelated_values_when_type_is_missing_or_unknown()
    {
        using var temp = new TempDirectory();
        string olderPath = Path.Combine(temp.Path, "older.json");
        File.WriteAllText(olderPath, """
            { "ServerUrl": "https://api.example.test", "DeviceName": "Desk", "AutoSync": true, "IntervalMinutes": 15 }
            """);

        AppSettings older = AppSettings.Load(olderPath);
        older.DeviceType.ShouldBeNull();
        older.ServerUrl.ShouldBe("https://api.example.test");
        older.DeviceName.ShouldBe("Desk");
        older.AutoSync.ShouldBeTrue();
        older.IntervalMinutes.ShouldBe(15);

        string futurePath = Path.Combine(temp.Path, "future.json");
        File.WriteAllText(futurePath, """
            { "ServerUrl": "https://api.example.test", "DeviceName": "Desk", "AutoSync": true, "DeviceType": "Handheld" }
            """);

        AppSettings future = AppSettings.Load(futurePath);
        future.DeviceType.ShouldBeNull();
        future.ServerUrl.ShouldBe("https://api.example.test");
        future.DeviceName.ShouldBe("Desk");
        future.AutoSync.ShouldBeTrue();

        future.DeviceType = DeviceType.Unknown;
        future.Save(futurePath);
        File.ReadAllText(futurePath).ShouldContain("\"DeviceType\": \"Unknown\"");
        AppSettings.Load(futurePath).DeviceType.ShouldBe(DeviceType.Unknown);
    }

    [Fact]
    public void Deck_suggestion_requires_an_explicit_save()
    {
        DeviceType? selection = DeviceTypeSetup.GetInitialSelection(null, DeviceType.SteamDeck);

        selection.ShouldBe(DeviceType.SteamDeck);
        DeviceTypeSetup.IsComplete(null).ShouldBeFalse("a highlighted Deck suggestion is not persisted truth");
        DeviceTypeSetup.GetInitialSelection(null, DeviceType.Desktop).ShouldBeNull();
    }

    [Fact]
    public async Task Incomplete_setup_blocks_connect_preview_and_auto_sync_without_outbound_work()
    {
        var counters = new OutboundCounters();
        var settings = new AppSettings
        {
            DeviceName = "Desk",
            AutoSync = true,
            HostedUploadConsentVersion = HostedProjection.ProjectionVersion,
        };
        using var agent = CreateAgent(settings, counters);

        agent.Start();
        await agent.ConnectAsync();
        (await agent.PrepareSyncAsync()).ShouldBeNull();
        await Task.Delay(50);

        counters.ConnectCount.ShouldBe(0);
        counters.BuildCount.ShouldBe(0);
        counters.UploadCount.ShouldBe(0);
    }

    [Fact]
    public async Task Setup_reset_blocks_uploading_a_prepared_body()
    {
        var counters = new OutboundCounters();
        using var agent = CreateAgent(new AppSettings
        {
            DeviceName = "Desk",
            DeviceType = DeviceType.Desktop,
        }, counters);

        PreparedSync prepared = (await agent.PrepareSyncAsync()).ShouldNotBeNull();
        agent.UpdateSettings(current => current.DeviceType = null);
        await agent.SyncNowAsync(prepared);

        counters.UploadCount.ShouldBe(0, "a stale prepared body cannot bypass a later setup reset");
    }

    [Theory]
    [InlineData(DeviceType.Desktop)]
    [InlineData(DeviceType.Laptop)]
    [InlineData(DeviceType.SteamDeck)]
    [InlineData(DeviceType.Unknown)]
    public async Task Each_explicit_choice_reaches_the_prepared_hosted_snapshot(DeviceType type)
    {
        var counters = new OutboundCounters();
        using var agent = CreateAgent(new AppSettings { DeviceName = "Desk", DeviceType = type }, counters);

        PreparedSync prepared = (await agent.PrepareSyncAsync()).ShouldNotBeNull();

        prepared.Upload.Snapshot.Device.Type.ShouldBe(type);
        counters.ObservedTypes.ShouldBe([type]);
    }

    [Fact]
    public async Task Later_type_change_affects_the_next_snapshot_without_changing_identity_or_token_state()
    {
        var counters = new OutboundCounters();
        using var agent = CreateAgent(new AppSettings
        {
            DeviceName = "Desk",
            DeviceType = DeviceType.Desktop,
        }, counters);

        PreparedSync desktop = (await agent.PrepareSyncAsync()).ShouldNotBeNull();
        agent.UpdateSettings(settings => settings.DeviceType = DeviceType.Laptop);
        PreparedSync laptop = (await agent.PrepareSyncAsync()).ShouldNotBeNull();

        desktop.Upload.Snapshot.Device.Id.ShouldBe(laptop.Upload.Snapshot.Device.Id);
        desktop.Upload.Snapshot.Device.Name.ShouldBe(laptop.Upload.Snapshot.Device.Name);
        desktop.Upload.Snapshot.Device.Type.ShouldBe(DeviceType.Desktop);
        laptop.Upload.Snapshot.Device.Type.ShouldBe(DeviceType.Laptop);
        agent.IsConnected.ShouldBeTrue();
        counters.ObservedTypes.ShouldBe([DeviceType.Desktop, DeviceType.Laptop]);
    }

    private static SyncAgent CreateAgent(AppSettings settings, OutboundCounters counters) =>
        new(settings, "upload-token", new SyncAgentDependencies
        {
            BuildSnapshotAsync = (options, _) =>
            {
                counters.BuildCount++;
                counters.ObservedTypes.Add(options.DeviceType);
                return Task.FromResult(BuildResult(options));
            },
            ConnectAsync = (_, _, _, _) =>
            {
                counters.ConnectCount++;
                return Task.FromResult<ConnectFlowResult?>(null);
            },
            UploadAsync = (_, _, _, _) =>
            {
                counters.UploadCount++;
                return Task.FromResult(new UploadResult
                {
                    Status = UploadStatus.Success,
                    ErrorCode = UploadErrorCode.None,
                });
            },
            LoadToken = () => "upload-token",
            ClearToken = () => { },
            SaveSettings = _ => { },
            ApplyAutoStart = _ => { },
        });

    private static SnapshotBuildResult BuildResult(SnapshotBuildOptions options)
    {
        DeviceType type = options.DeviceType ?? DeviceType.Unknown;
        var device = new SnapshotDevice
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = options.DeviceName ?? "Shelfbound device",
            Type = type,
            Os = OsPlatform.Windows,
        };
        var snapshot = new SnapshotDocument
        {
            SchemaVersion = SnapshotSchema.Version,
            SnapshotId = "22222222-2222-2222-2222-222222222222",
            CreatedAt = DateTimeOffset.UtcNow,
            Source = new SnapshotSource { Tool = "test", ToolVersion = "1", Platform = OsPlatform.Windows },
            Device = device,
            Stats = new SnapshotStats { LibraryCount = 0, InstalledGameCount = 0, TotalSizeOnDiskBytes = 0 },
        };
        return new SnapshotBuildResult(snapshot, device, []);
    }

    private sealed class OutboundCounters
    {
        public int BuildCount { get; set; }
        public int ConnectCount { get; set; }
        public int UploadCount { get; set; }
        public List<DeviceType?> ObservedTypes { get; } = [];
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shelfbound-tray-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* test cleanup only */ }
        }
    }
}
