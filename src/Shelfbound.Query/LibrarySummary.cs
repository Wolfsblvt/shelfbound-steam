using Shelfbound.Core.Model;

namespace Shelfbound.Query;

/// <summary>A high-level overview of the library for an at-a-glance answer.</summary>
public sealed record LibrarySummary
{
    public required int TotalGames { get; init; }
    public required int InstalledGames { get; init; }
    public required int CategorizedGames { get; init; }
    public required long TotalSizeOnDiskBytes { get; init; }

    /// <summary>
    /// Whether <see cref="TotalGames"/> is the full owned library or only what's installed. When
    /// <see cref="LibraryScope.InstalledOnly"/>, owned-but-not-installed games are absent, so a missing
    /// game does not mean the user doesn't own it.
    /// </summary>
    public LibraryScope Scope { get; init; } = LibraryScope.InstalledOnly;

    /// <summary>Total playtime in minutes, or null if the snapshot wasn't enriched with playtime.</summary>
    public long? TotalPlaytimeMinutes { get; init; }

    public required IReadOnlyList<SnapshotCategory> Categories { get; init; }
    public required IReadOnlyList<SnapshotLibrary> Libraries { get; init; }
}
