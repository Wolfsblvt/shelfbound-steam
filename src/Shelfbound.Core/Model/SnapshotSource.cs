namespace Shelfbound.Core.Model;

/// <summary>Identifies the tool that produced a snapshot. Carries no user data.</summary>
public sealed record SnapshotSource
{
    public required string Tool { get; init; }
    public required string ToolVersion { get; init; }
    public required OsPlatform Platform { get; init; }
}
