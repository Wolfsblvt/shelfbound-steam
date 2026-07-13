using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class StorageClassifierTests
{
    [Theory]
    [InlineData(DriveType.Fixed, StorageKind.Internal)]
    [InlineData(DriveType.Removable, StorageKind.External)] // SD-vs-USB is indistinguishable here → external, not a guess
    [InlineData(DriveType.Network, StorageKind.Network)]
    [InlineData(DriveType.CDRom, StorageKind.Unknown)]
    [InlineData(DriveType.Ram, StorageKind.Unknown)]
    [InlineData(DriveType.NoRootDirectory, StorageKind.Unknown)]
    [InlineData(DriveType.Unknown, StorageKind.Unknown)]
    public void Maps_drive_type_to_contract_storage_kind(DriveType driveType, StorageKind expected)
    {
        StorageClassifier.Classify(driveType).ShouldBe(expected);
    }

    [Fact]
    public void Describe_reads_the_current_drive_as_a_valid_storage_kind()
    {
        // Best-effort against a real drive: kind always resolves; sizes are consistent when readable.
        var storage = StorageClassifier.Describe(AppContext.BaseDirectory);

        storage.ShouldNotBeNull();
        storage!.Kind.ShouldBeOneOf(Enum.GetValues<StorageKind>());
        if (storage.FreeBytes is { } free && storage.TotalBytes is { } total)
        {
            free.ShouldBeGreaterThanOrEqualTo(0);
            total.ShouldBeGreaterThanOrEqualTo(free);
        }
    }
}
