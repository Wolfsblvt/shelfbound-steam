namespace Shelfbound.Core.Model;

/// <summary>
/// A Steam library folder, abstracted to its index and label. Full filesystem paths are
/// deliberately omitted from the snapshot contract for privacy. See docs/project/privacy-and-data.md.
/// </summary>
public sealed record SnapshotLibrary
{
    public required int Index { get; init; }

    /// <summary>User/Steam label for the library, or a synthesized fallback (e.g. "library-1").</summary>
    public required string Label { get; init; }

    public required int GameCount { get; init; }

    /// <summary>
    /// Optional storage-medium facts (kind + free/total bytes). Additive; may be null. Carries no
    /// filesystem path — kind + sizes only.
    /// </summary>
    public SnapshotStorage? Storage { get; init; }
}
