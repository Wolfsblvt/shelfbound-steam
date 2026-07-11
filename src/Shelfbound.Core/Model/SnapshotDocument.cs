namespace Shelfbound.Core.Model;

/// <summary>
/// The root Shelfbound snapshot: the complete portable, versioned contract between the local scanner,
/// local MCP server, exporters, and other consumers. Official hosted clients derive a minimized
/// projection from this personal local document rather than uploading it verbatim.
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

    /// <summary>The user's local Steam collections/categories with game counts (their library vocabulary).</summary>
    public IReadOnlyList<SnapshotCategory> Categories { get; init; } = [];

    public required SnapshotStats Stats { get; init; }
}
