using Shelfbound.Core.Model;

namespace Shelfbound.Steam.Steam;

/// <summary>Inputs for a single scan of a local Steam installation.</summary>
public sealed record SteamScanRequest
{
    public required string SteamRootPath { get; init; }
    public required SnapshotDevice Device { get; init; }
    public required string ToolVersion { get; init; }
}

/// <summary>Result of a scan: the produced snapshot plus any non-fatal warnings.</summary>
public sealed record ScanResult
{
    public required SnapshotDocument Snapshot { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
