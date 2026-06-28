namespace Shelfbound.Core.Model;

/// <summary>
/// The root Shelfbound snapshot: the portable, versioned contract between the local scanner,
/// the local MCP server, and (later) hosted ingestion. This type is the architectural seam that
/// lets every producer/consumer interoperate without sharing parsing code.
/// See docs/project/snapshot-schema.md.
/// </summary>
public sealed record SnapshotDocument
{
    /// <summary>Semantic version of the snapshot schema this document conforms to.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Unique id for this snapshot instance.</summary>
    public required string SnapshotId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required SnapshotSource Source { get; init; }
    public required SnapshotDevice Device { get; init; }

    public IReadOnlyList<SteamAccount> SteamAccounts { get; init; } = [];
    public IReadOnlyList<SnapshotLibrary> Libraries { get; init; } = [];
    public IReadOnlyList<SnapshotGame> Games { get; init; } = [];

    public required SnapshotStats Stats { get; init; }
}
