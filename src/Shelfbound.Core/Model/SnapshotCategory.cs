namespace Shelfbound.Core.Model;

/// <summary>A local Steam collection/category and how many of the user's games carry it.</summary>
public sealed record SnapshotCategory
{
    public required string Name { get; init; }

    /// <summary>Number of categorized (owned) games tagged with this category.</summary>
    public required int GameCount { get; init; }
}
