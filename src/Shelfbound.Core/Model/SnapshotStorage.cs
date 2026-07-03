namespace Shelfbound.Core.Model;

/// <summary>
/// Optional per-library storage facts: the medium kind plus best-effort free/total capacity. Additive
/// to the contract (schema >= 0.5.0) — snapshots without it stay valid and consumers must be lenient.
/// Powers device-aware views ("fits on your SD card", "free up space") without exposing a filesystem
/// path: <b>kind + sizes only, never a path</b>. See docs/project/snapshot-schema.md and privacy-and-data.md.
/// </summary>
public sealed record SnapshotStorage
{
    /// <summary>Storage-medium kind. <see cref="StorageKind.Unknown"/> when the OS can't tell — never a guess.</summary>
    public required StorageKind Kind { get; init; }

    /// <summary>Free space in bytes on the backing filesystem (best-effort; null when unavailable).</summary>
    public long? FreeBytes { get; init; }

    /// <summary>Total capacity in bytes of the backing filesystem (best-effort; null when unavailable).</summary>
    public long? TotalBytes { get; init; }
}
