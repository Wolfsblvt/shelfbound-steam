namespace Shelfbound.Core.Model;

/// <summary>
/// The device a snapshot was taken on. <see cref="Id"/> is a locally generated, random,
/// non-identifying value (not derived from hardware or account data) so devices can be told
/// apart without leaking machine identity. See docs/project/privacy-and-data.md.
/// </summary>
public sealed record SnapshotDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DeviceType Type { get; init; }
    public required OsPlatform Os { get; init; }
}
