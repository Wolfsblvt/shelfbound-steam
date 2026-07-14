using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SnapshotSerializerTests
{
    [Fact]
    public void Round_trips_a_snapshot_and_uses_camel_case_enums()
    {
        var snapshot = new SnapshotDocument
        {
            SchemaVersion = SnapshotSchema.Version,
            SnapshotId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow,
            Source = new SnapshotSource { Tool = "t", ToolVersion = "0.1.0", Platform = OsPlatform.Windows },
            Device = new SnapshotDevice { Id = "id", Name = "PC", Type = DeviceType.SteamDeck, Os = OsPlatform.Linux },
            Games = [new SnapshotGame { AppId = 1, Name = "G", Installed = true, LibraryIndex = 0 }],
            Stats = new SnapshotStats
            {
                LibraryCount = 1,
                InstalledGameCount = 1,
                TotalSizeOnDiskBytes = 10,
                Scope = LibraryScope.ObservedSubset,
            },
        };

        string json = SnapshotSerializer.Serialize(snapshot);

        json.ShouldContain("\"steamDeck\""); // enums serialize as camelCase strings
        json.ShouldContain("\"observedSubset\"");

        // Re-serializing the round-tripped document must reproduce the original JSON exactly.
        SnapshotSerializer.Serialize(SnapshotSerializer.Deserialize(json)).ShouldBe(json);
    }

    [Fact]
    public void Preserves_published_library_scope_ordinals_without_using_them_as_coverage_rank()
    {
        ((int)LibraryScope.InstalledOnly).ShouldBe(0);
        ((int)LibraryScope.FullLibrary).ShouldBe(1);
        ((int)LibraryScope.ObservedSubset).ShouldBe(2);

        LibraryScopeSemantics.IsBroaderThan(
            LibraryScope.ObservedSubset,
            LibraryScope.InstalledOnly).ShouldBeTrue();
        LibraryScopeSemantics.IsBroaderThan(
            LibraryScope.FullLibrary,
            LibraryScope.ObservedSubset).ShouldBeTrue();
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.5.9")]
    [InlineData("0.5.0+producer")]
    public void Legacy_full_library_reports_are_operationally_partial(string schemaVersion)
    {
        LibraryScopeSemantics.GetOperationalScope(schemaVersion, LibraryScope.FullLibrary)
            .ShouldBe(LibraryScope.ObservedSubset);
    }

    [Fact]
    public void Current_full_library_reports_keep_their_completeness_contract()
    {
        LibraryScopeSemantics.GetOperationalScope("0.6.0", LibraryScope.FullLibrary)
            .ShouldBe(LibraryScope.FullLibrary);
    }

    [Fact]
    public void Round_trips_per_library_storage_and_omits_it_when_absent()
    {
        var snapshot = new SnapshotDocument
        {
            SchemaVersion = SnapshotSchema.Version,
            SnapshotId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow,
            Source = new SnapshotSource { Tool = "t", ToolVersion = "0.1.0", Platform = OsPlatform.Linux },
            Device = new SnapshotDevice { Id = "id", Name = "Deck", Type = DeviceType.SteamDeck, Os = OsPlatform.Linux },
            Libraries =
            [
                new SnapshotLibrary
                {
                    Index = 0, Label = "SD Card", GameCount = 3,
                    Storage = new SnapshotStorage { Kind = StorageKind.SdCard, FreeBytes = 111, TotalBytes = 999 },
                },
                new SnapshotLibrary { Index = 1, Label = "internal", GameCount = 2 }, // no storage → omitted
            ],
            Stats = new SnapshotStats { LibraryCount = 2, InstalledGameCount = 5, TotalSizeOnDiskBytes = 42 },
        };

        string json = SnapshotSerializer.Serialize(snapshot);

        json.ShouldContain("\"sdCard\""); // StorageKind serializes as a camelCase string
        json.ShouldContain("\"freeBytes\": 111");

        var restored = SnapshotSerializer.Deserialize(json);
        var withStorage = restored.Libraries.Single(l => l.Index == 0);
        withStorage.Storage.ShouldNotBeNull();
        withStorage.Storage!.Kind.ShouldBe(StorageKind.SdCard);
        withStorage.Storage.FreeBytes.ShouldBe(111);
        withStorage.Storage.TotalBytes.ShouldBe(999);
        // Absent storage stays absent (null omitted, not written) and round-trips identically.
        restored.Libraries.Single(l => l.Index == 1).Storage.ShouldBeNull();
        SnapshotSerializer.Serialize(restored).ShouldBe(json);
    }
}
