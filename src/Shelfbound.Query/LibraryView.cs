using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;

namespace Shelfbound.Query;

/// <summary>The merged library: per-game facts + user-data, plus categories and their user meanings.</summary>
public sealed record LibraryView
{
    public required IReadOnlyList<LibraryGame> Games { get; init; }
    public required IReadOnlyList<SnapshotCategory> Categories { get; init; }
    public required IReadOnlyList<SnapshotLibrary> Libraries { get; init; }

    /// <summary>The device context for this view (drives device-aware recommendations). For a merged
    /// multi-device view this is device-agnostic, so device-specific suggestions stay conservative.</summary>
    public SnapshotDevice? Device { get; init; }

    /// <summary>Whether this view covers the full owned library or only installed games. Surfaced so
    /// consumers don't read "not found" as "not owned" when the snapshot is installed-only.</summary>
    public LibraryScope Scope { get; init; } = LibraryScope.InstalledOnly;
    public IReadOnlyDictionary<string, CategoryDefinition> CategoryDefinitions { get; init; }
        = new Dictionary<string, CategoryDefinition>();
    public IReadOnlyList<Memory> GlobalMemories { get; init; } = [];

    /// <summary>When Shelfbound first scanned this owner's library — the baseline for "recently added".</summary>
    public DateTimeOffset? FirstScanAt { get; init; }
}
