namespace Shelfbound.Core.Model;

/// <summary>Convenience aggregates so consumers don't have to recompute basics.</summary>
public sealed record SnapshotStats
{
    public required int LibraryCount { get; init; }
    public required int InstalledGameCount { get; init; }
    public required long TotalSizeOnDiskBytes { get; init; }

    /// <summary>
    /// Coverage of the game observations. Defaults to <see cref="LibraryScope.InstalledOnly"/> so
    /// older or unenriched snapshots never imply facts beyond local installed presence. Steam Web API
    /// enrichment produces <see cref="LibraryScope.ObservedSubset"/>; only a source with an explicit
    /// completeness contract may produce <see cref="LibraryScope.FullLibrary"/>.
    /// </summary>
    public LibraryScope Scope { get; init; } = LibraryScope.InstalledOnly;
}
