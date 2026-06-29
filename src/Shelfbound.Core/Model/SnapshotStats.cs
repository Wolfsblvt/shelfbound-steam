namespace Shelfbound.Core.Model;

/// <summary>Convenience aggregates so consumers don't have to recompute basics.</summary>
public sealed record SnapshotStats
{
    public required int LibraryCount { get; init; }
    public required int InstalledGameCount { get; init; }
    public required long TotalSizeOnDiskBytes { get; init; }

    /// <summary>
    /// Whether the game list covers the full owned library or only installed games. Defaults to
    /// <see cref="LibraryScope.InstalledOnly"/> (the conservative assumption) so older/un-enriched
    /// snapshots never falsely imply completeness. Set to <see cref="LibraryScope.FullLibrary"/> once
    /// Steam Web API enrichment has added owned-but-not-installed games.
    /// </summary>
    public LibraryScope Scope { get; init; } = LibraryScope.InstalledOnly;
}
