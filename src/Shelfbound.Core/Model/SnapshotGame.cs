namespace Shelfbound.Core.Model;

/// <summary>
/// A single game entry in a snapshot. The v0 scanner only emits Steam apps that are present in a
/// local library (i.e. installed). Owned-but-not-installed games require the Steam Web API and are
/// a planned addition. See docs/project/snapshot-schema.md.
/// </summary>
public sealed record SnapshotGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required bool Installed { get; init; }
    public required int LibraryIndex { get; init; }

    /// <summary>Relative install folder name under steamapps/common (not a full path). Optional.</summary>
    public string? InstallDir { get; init; }

    public long? SizeOnDiskBytes { get; init; }
    public DateTimeOffset? LastUpdated { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>
    /// Local Steam collection/category names the user assigned to this game (their personal library
    /// organization), in Steam's tag order. Empty if the game is uncategorized.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
}
