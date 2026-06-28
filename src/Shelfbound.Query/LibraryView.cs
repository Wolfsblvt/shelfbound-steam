using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>The merged library: per-game facts + user-data, plus categories and their user meanings.</summary>
public sealed record LibraryView
{
    public required IReadOnlyList<LibraryGame> Games { get; init; }
    public required IReadOnlyList<SnapshotCategory> Categories { get; init; }
    public required IReadOnlyList<SnapshotLibrary> Libraries { get; init; }
    public IReadOnlyDictionary<string, CategoryDefinition> CategoryDefinitions { get; init; }
        = new Dictionary<string, CategoryDefinition>();
    public IReadOnlyList<Memory> GlobalMemories { get; init; } = [];

    /// <summary>When Shelfbound first scanned this owner's library — the baseline for "recently added".</summary>
    public DateTimeOffset? FirstScanAt { get; init; }
}
