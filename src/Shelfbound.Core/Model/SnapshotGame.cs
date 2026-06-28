namespace Shelfbound.Core.Model;

/// <summary>
/// A single game in the library. The local scan emits installed games; Steam Web API enrichment adds
/// owned-but-not-installed games (<see cref="Installed"/> = false, <see cref="LibraryIndex"/> = null)
/// and playtime. Every entry is a game the user owns. See docs/project/snapshot-schema.md.
/// </summary>
public sealed record SnapshotGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required bool Installed { get; init; }

    /// <summary>Index of the local library the game is installed in, or null if it is not installed locally.</summary>
    public int? LibraryIndex { get; init; }

    /// <summary>Relative install folder name under steamapps/common (not a full path). Optional.</summary>
    public string? InstallDir { get; init; }

    public long? SizeOnDiskBytes { get; init; }

    /// <summary>Total playtime in minutes (from the Steam Web API; null if the snapshot wasn't enriched).</summary>
    public long? PlaytimeMinutes { get; init; }

    public DateTimeOffset? LastUpdated { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>
    /// Local Steam collection/category names the user assigned to this game (their personal library
    /// organization), in Steam's tag order. Empty if the game is uncategorized.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
}
