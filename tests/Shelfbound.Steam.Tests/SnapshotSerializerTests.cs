using Shouldly;
using Shelfbound.Core;
using Shelfbound.Core.Model;

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
            Stats = new SnapshotStats { LibraryCount = 1, InstalledGameCount = 1, TotalSizeOnDiskBytes = 10 },
        };

        string json = SnapshotSerializer.Serialize(snapshot);

        json.ShouldContain("\"steamDeck\""); // enums serialize as camelCase strings

        // Re-serializing the round-tripped document must reproduce the original JSON exactly.
        SnapshotSerializer.Serialize(SnapshotSerializer.Deserialize(json)).ShouldBe(json);
    }
}
