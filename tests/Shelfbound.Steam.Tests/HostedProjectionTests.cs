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
            .ShouldContain("legacy false-full");
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
    public void Preserves_a_legacy_scope_label_to_keep_its_schema_identity_immutable()
    {
        SnapshotDocument local = FullLocalSnapshot("Legacy device");
        SnapshotDocument legacy = local with
        {
            SchemaVersion = "0.5.0",
            Stats = local.Stats with { Scope = LibraryScope.FullLibrary },
        };

        HostedUpload upload = HostedProjection.Prepare(legacy);

        upload.Snapshot.SchemaVersion.ShouldBe("0.5.0");
        upload.Snapshot.Stats.Scope.ShouldBe(LibraryScope.FullLibrary);
        upload.Json.ShouldContain("\"schemaVersion\":\"0.5.0\"");
        upload.Json.ShouldContain("\"scope\":\"fullLibrary\"");
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

    [Fact]
    public void Disabled_or_non_matching_exclusion_preserves_the_existing_bytes_and_scope()
    {
        SnapshotDocument local = PrivateExclusionSnapshot();
        string expected = PrivateExclusionGolden("disabled");

        PrivateGameUploadPreparation disabled = PrivateGameUploadPreparer.Prepare(
            local,
            enabled: false,
            new HashSet<int>(),
            steamPath: null,
            machineName: "synthetic-host");
        PrivateGameUploadPreparation noMatch = PrivateGameUploadPreparer.PrepareFromEvidence(
            local,
            new HashSet<int> { 999 },
            new HashSet<int>());

        disabled.Upload.Json.ShouldBe(expected);
        noMatch.Upload.Json.ShouldBe(expected);
        disabled.Status.Enabled.ShouldBeFalse();
        noMatch.SkippedGames.ShouldBeEmpty();
        noMatch.Upload.Snapshot.Stats.Scope.ShouldBe(LibraryScope.FullLibrary);
    }

    [Fact]
    public void Positive_membership_omits_except_for_local_override_and_recomputes_every_aggregate()
    {
        SnapshotDocument local = PrivateExclusionSnapshot();
        string originalLocalBytes = SnapshotSerializer.Serialize(local, indented: false);

        PrivateGameUploadPreparation prepared = PrivateGameUploadPreparer.PrepareFromEvidence(
            local,
            new HashSet<int> { 20, 40 },
            new HashSet<int> { 40 });

        prepared.Upload.Json.ShouldBe(PrivateExclusionGolden("partial"));
        prepared.SkippedGames.ShouldHaveSingleItem().ShouldBe(new SkippedPrivateGame(20, "Skipped Beta"));
        prepared.Upload.Snapshot.Libraries.Select(library => library.GameCount).ShouldBe([1, 1]);
        prepared.Upload.Snapshot.Categories.Select(category => (category.Name, category.GameCount)).ShouldBe(
            [("Alpha", 2), ("Beta", 1), ("Shared", 1)]);
        prepared.Upload.Snapshot.Stats.LibraryCount.ShouldBe(2, "physical library facts survive omission");
        prepared.Upload.Snapshot.Stats.InstalledGameCount.ShouldBe(2);
        prepared.Upload.Snapshot.Stats.TotalSizeOnDiskBytes.ShouldBe(400);
        prepared.Upload.Snapshot.Stats.Scope.ShouldBe(LibraryScope.ObservedSubset);
        SnapshotSerializer.Serialize(local, indented: false).ShouldBe(originalLocalBytes);

        using JsonDocument json = JsonDocument.Parse(prepared.Upload.Json);
        json.RootElement.TryGetProperty("steamAccounts", out _).ShouldBeFalse();
        prepared.Upload.Json.ShouldNotContain("isPrivate");
        prepared.Upload.Json.ShouldNotContain("exclusion");
        prepared.Upload.Json.ShouldNotContain("evidence");
        prepared.Upload.Json.ShouldNotContain("reason");
    }

    [Fact]
    public void All_games_can_be_omitted_without_erasing_library_facts_or_claiming_full_coverage()
    {
        SnapshotDocument local = PrivateExclusionSnapshot();

        PrivateGameUploadPreparation prepared = PrivateGameUploadPreparer.PrepareFromEvidence(
            local,
            new HashSet<int> { 10, 20, 30, 40 },
            new HashSet<int>());

        prepared.Upload.Json.ShouldBe(PrivateExclusionGolden("all"));
        prepared.Upload.Snapshot.Games.ShouldBeEmpty();
        prepared.Upload.Snapshot.Categories.ShouldBeEmpty();
        prepared.Upload.Snapshot.Libraries.Select(library => library.GameCount).ShouldBe([0, 0]);
        prepared.Upload.Snapshot.Stats.LibraryCount.ShouldBe(2);
        prepared.Upload.Snapshot.Stats.Scope.ShouldBe(LibraryScope.ObservedSubset);
    }

    [Fact]
    public void Existing_partial_scopes_are_never_rewritten_and_unskip_regenerates_exact_bytes()
    {
        SnapshotDocument full = PrivateExclusionSnapshot();
        foreach (LibraryScope scope in new[] { LibraryScope.InstalledOnly, LibraryScope.ObservedSubset })
        {
            SnapshotDocument partial = full with { Stats = full.Stats with { Scope = scope } };
            PrivateGameUploadPreparation prepared = PrivateGameUploadPreparer.PrepareFromEvidence(
                partial,
                new HashSet<int> { 20 },
                new HashSet<int>());

            prepared.Upload.Snapshot.Stats.Scope.ShouldBe(scope);
        }

        PrivateGameUploadPreparation initial = PrivateGameUploadPreparer.PrepareFromEvidence(
            full,
            new HashSet<int> { 20 },
            new HashSet<int>());
        PrivateGameUploadPreparation overridden = initial.WithUnskippedAppIds(new HashSet<int> { 20 });

        overridden.Upload.Json.ShouldBe(PrivateExclusionGolden("disabled"));
        overridden.SkippedGames.ShouldBeEmpty();
        overridden.Status.Message.ShouldContain("device-local overrides");
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

    private static SnapshotDocument PrivateExclusionSnapshot() =>
        SnapshotSerializer.Deserialize(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "private-game-exclusion.input.json")));

    private static string PrivateExclusionGolden(string action) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            $"private-game-exclusion.{action}.golden.json")).TrimEnd('\r', '\n');

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
