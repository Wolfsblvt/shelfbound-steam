namespace Shelfbound.Core.UserData;

/// <summary>The user's personal meaning for a local Steam collection/category name (e.g. what "Hold" means).</summary>
public sealed record CategoryDefinition
{
    public required string Name { get; init; }
    public required string Meaning { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
