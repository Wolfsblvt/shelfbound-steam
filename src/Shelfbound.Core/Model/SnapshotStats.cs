namespace Shelfbound.Core.Model;

/// <summary>Convenience aggregates so consumers don't have to recompute basics.</summary>
public sealed record SnapshotStats
{
    public required int LibraryCount { get; init; }
    public required int InstalledGameCount { get; init; }
    public required long TotalSizeOnDiskBytes { get; init; }
}
