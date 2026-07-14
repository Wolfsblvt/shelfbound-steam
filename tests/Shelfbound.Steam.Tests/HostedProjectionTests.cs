using System.Text.Json;
using Shelfbound.Client;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class HostedProjectionTests
{
    [Fact]
    public void Coverage_semantics_require_projection_consent_version_two()
    {
        HostedProjection.ProjectionVersion.ShouldBe("2");
        HostedProjection.FieldPurposes.Single(field => field.Path == "stats.scope").Purpose
            .ShouldContain("non-complete observed subset");
    }

    [Fact]
    public void Projects_the_exact_golden_shape_and_drops_local_account_identity()
    {
        SnapshotDocument local = FullLocalSnapshot("Living room PC");

        HostedUpload upload = HostedProjection.Prepare(local);

        string golden = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "hosted-snapshot.golden.json")).TrimEnd('\r', '\n');
        upload.Json.ShouldBe(golden);
        upload.Json.ShouldNotContain("steamAccounts");
        upload.Json.ShouldNotContain("synthetic-login");
        upload.Json.ShouldNotContain("Synthetic Persona");
        upload.Json.ShouldNotContain("76561198000000001");
        upload.Json.ShouldNotContain("Windows 10.0.26200");

        HostedSnapshot restored = HostedProjection.Deserialize(upload.Json);
        HostedProjection.Serialize(restored).ShouldBe(upload.Json);
        restored.Device.Name.ShouldBe("Living room PC");
        restored.Device.Specs!.Cpu.ShouldBe("Synthetic CPU");
        restored.Games.Single().InstallDir.ShouldBe("Relative Folder");
        restored.Libraries.Single().Storage!.Kind.ShouldBe(StorageKind.Internal);
    }

    [Fact]
    public void Neutralizes_a_legacy_machine_name_before_upload()
    {
        const string syntheticHostname = "synthetic-private-host";
        SnapshotDocument local = FullLocalSnapshot(syntheticHostname);

        HostedUpload upload = HostedProjection.Prepare(local, syntheticHostname);

        upload.Snapshot.Device.Name.ShouldBe(DeviceIdentity.DefaultDeviceName);
        upload.Json.ShouldNotContain(syntheticHostname, Case.Insensitive);
    }

    [Fact]
    public void Projects_a_legacy_false_full_library_report_as_an_observed_subset()
    {
        SnapshotDocument local = FullLocalSnapshot("Legacy device");
        SnapshotDocument legacy = local with
        {
            SchemaVersion = "0.5.0",
            Stats = local.Stats with { Scope = LibraryScope.FullLibrary },
        };

        HostedUpload upload = HostedProjection.Prepare(legacy);

        upload.Snapshot.Stats.Scope.ShouldBe(LibraryScope.ObservedSubset);
        legacy.Stats.Scope.ShouldBe(LibraryScope.FullLibrary);
    }

    [Fact]
    public void Purpose_manifest_covers_every_surviving_golden_leaf()
    {
        HostedUpload upload = HostedProjection.Prepare(FullLocalSnapshot("Living room PC"));
        using JsonDocument document = JsonDocument.Parse(upload.Json);
        var leaves = new HashSet<string>(StringComparer.Ordinal);

        CollectLeafPaths(document.RootElement, "", leaves);

        string[] manifestPaths = HostedProjection.FieldPurposes.Select(field => field.Path).ToArray();
        manifestPaths.Length.ShouldBe(manifestPaths.Distinct(StringComparer.Ordinal).Count());
        manifestPaths.ToHashSet(StringComparer.Ordinal).ShouldBe(leaves);
    }

    private static SnapshotDocument FullLocalSnapshot(string deviceName) => new()
    {
        SchemaVersion = SnapshotSchema.Version,
        SnapshotId = "11111111-1111-1111-1111-111111111111",
        CreatedAt = DateTimeOffset.Parse("2026-07-11T12:34:56+00:00"),
        Source = new SnapshotSource
        {
            Tool = "contract-test",
            ToolVersion = "1.2.3",
            Platform = OsPlatform.Windows,
        },
        Device = new SnapshotDevice
        {
            Id = "22222222-2222-2222-2222-222222222222",
            Name = deviceName,
            Type = DeviceType.Desktop,
            Os = OsPlatform.Windows,
            Specs = new DeviceSpecs
            {
                Cpu = "Synthetic CPU",
                LogicalCores = 8,
                TotalMemoryBytes = 16_000_000_000,
                Gpu = "Synthetic GPU",
                OsDescription = "Microsoft Windows 10.0.26200",
                Architecture = "X64",
            },
        },
        SteamAccounts =
        [
            new SteamAccount
            {
                SteamId64 = "76561198000000001",
                AccountName = "synthetic-login",
                PersonaName = "Synthetic Persona",
                MostRecent = true,
            },
        ],
        Libraries =
        [
            new SnapshotLibrary
            {
                Index = 0,
                Label = "Main library",
                GameCount = 1,
                Storage = new SnapshotStorage
                {
                    Kind = StorageKind.Internal,
                    FreeBytes = 123_456_789,
                    TotalBytes = 987_654_321,
                },
            },
        ],
        Games =
        [
            new SnapshotGame
            {
                AppId = 42,
                Name = "Private shortcut title",
                Installed = true,
                LibraryIndex = 0,
                InstallDir = "Relative Folder",
                SizeOnDiskBytes = 12_345,
                PlaytimeMinutes = 67,
                LastUpdated = DateTimeOffset.Parse("2026-07-10T10:00:00+00:00"),
                LastPlayed = DateTimeOffset.Parse("2026-07-11T11:00:00+00:00"),
                Categories = ["Deck", "Private collection"],
            },
        ],
        Categories =
        [
            new SnapshotCategory { Name = "Deck", GameCount = 1 },
            new SnapshotCategory { Name = "Private collection", GameCount = 1 },
        ],
        Stats = new SnapshotStats
        {
            LibraryCount = 1,
            InstalledGameCount = 1,
            TotalSizeOnDiskBytes = 12_345,
            Scope = LibraryScope.ObservedSubset,
        },
    };

    private static void CollectLeafPaths(JsonElement element, string path, ISet<string> leaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string propertyPath = string.IsNullOrEmpty(path)
                        ? property.Name
                        : $"{path}.{property.Name}";
                    CollectLeafPaths(property.Value, propertyPath, leaves);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    CollectLeafPaths(item, $"{path}[]", leaves);
                break;
            default:
                leaves.Add(path);
                break;
        }
    }
}
