namespace Shelfbound.Steam.Steam;

/// <summary>The subset of an appmanifest_*.acf file that Shelfbound cares about.</summary>
public sealed record AppManifest
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required int StateFlags { get; init; }
    public string? InstallDir { get; init; }
    public long? SizeOnDisk { get; init; }
    public DateTimeOffset? LastUpdated { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>Steam StateFlag bit 4 (StateFullyInstalled).</summary>
    public bool IsFullyInstalled => (StateFlags & 4) == 4;
}
