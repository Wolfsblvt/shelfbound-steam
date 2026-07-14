using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Client;

/// <summary>A hosted-upload field and the product purpose that justifies transmitting it.</summary>
public sealed record HostedFieldPurpose(string Path, string Purpose);

/// <summary>
/// The minimized snapshot shape accepted by hosted ingestion. This is deliberately a separate,
/// whitelist-only object graph: adding a field to the local snapshot never makes it upload by accident.
/// </summary>
public sealed record HostedSnapshot
{
    public required string SchemaVersion { get; init; }
    public required string SnapshotId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required HostedSnapshotSource Source { get; init; }
    public required HostedSnapshotDevice Device { get; init; }
    public IReadOnlyList<HostedSnapshotLibrary> Libraries { get; init; } = [];
    public IReadOnlyList<HostedSnapshotGame> Games { get; init; } = [];
    public IReadOnlyList<HostedSnapshotCategory> Categories { get; init; } = [];
    public required HostedSnapshotStats Stats { get; init; }
}

public sealed record HostedSnapshotSource
{
    public required string Tool { get; init; }
    public required string ToolVersion { get; init; }
    public required OsPlatform Platform { get; init; }
}

public sealed record HostedSnapshotDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DeviceType Type { get; init; }
    public required OsPlatform Os { get; init; }
    public HostedDeviceSpecs? Specs { get; init; }
}

public sealed record HostedDeviceSpecs
{
    public string? Cpu { get; init; }
    public int? LogicalCores { get; init; }
    public long? TotalMemoryBytes { get; init; }
    public string? Gpu { get; init; }
    public string? OsDescription { get; init; }
    public string? Architecture { get; init; }
}

public sealed record HostedSnapshotLibrary
{
    public required int Index { get; init; }
    public required string Label { get; init; }
    public required int GameCount { get; init; }
    public HostedSnapshotStorage? Storage { get; init; }
}

public sealed record HostedSnapshotStorage
{
    public required StorageKind Kind { get; init; }
    public long? FreeBytes { get; init; }
    public long? TotalBytes { get; init; }
}

public sealed record HostedSnapshotGame
{
    public required int AppId { get; init; }
    public required string Name { get; init; }
    public required bool Installed { get; init; }
    public int? LibraryIndex { get; init; }
    public string? InstallDir { get; init; }
    public long? SizeOnDiskBytes { get; init; }
    public long? PlaytimeMinutes { get; init; }
    public DateTimeOffset? LastUpdated { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
}

public sealed record HostedSnapshotCategory
{
    public required string Name { get; init; }
    public required int GameCount { get; init; }
}

public sealed record HostedSnapshotStats
{
    public required int LibraryCount { get; init; }
    public required int InstalledGameCount { get; init; }
    public required long TotalSizeOnDiskBytes { get; init; }
    public required LibraryScope Scope { get; init; }
}

/// <summary>
/// A projection plus its canonical compact JSON. Preview and transport share this instance so the
/// bytes a user approves are the bytes sent to the server.
/// </summary>
public sealed record HostedUpload
{
    internal HostedUpload(HostedSnapshot snapshot, string json)
    {
        Snapshot = snapshot;
        Json = json;
    }

    public HostedSnapshot Snapshot { get; }
    public string Json { get; }
    public int GameCount => Snapshot.Games.Count;
}

/// <summary>Builds and serializes the privacy-minimized hosted upload contract.</summary>
public static class HostedProjection
{
    /// <summary>
    /// Version of the projection/consent contract. Increment when the uploaded field set expands or
    /// a field purpose materially changes. It is local client state, not part of the ingest wire shape.
    /// </summary>
    public const string ProjectionVersion = "2";

    /// <summary>The explicit purpose manifest for every leaf field that can leave the machine.</summary>
    public static IReadOnlyList<HostedFieldPurpose> FieldPurposes { get; } =
    [
        new("schemaVersion", "Selects the compatible snapshot contract."),
        new("snapshotId", "Identifies this captured snapshot for diagnostics and retry handling."),
        new("createdAt", "Records when the library state was observed."),
        new("source.tool", "Identifies the producer for compatibility diagnostics."),
        new("source.toolVersion", "Identifies producer behavior for compatibility diagnostics."),
        new("source.platform", "Records the producer OS family for compatibility diagnostics."),
        new("device.id", "Random, locally generated device key used to keep device snapshots separate."),
        new("device.name", "User-chosen device label used by token binding, device switching, and curation UI."),
        new("device.type", "Enables device-aware recommendations such as Steam Deck suitability."),
        new("device.os", "Enables OS compatibility and device-aware recommendations."),
        new("device.specs.cpu", "Supports performance-fit recommendations without hardware serials."),
        new("device.specs.logicalCores", "Supports performance-fit recommendations."),
        new("device.specs.totalMemoryBytes", "Supports memory-fit recommendations."),
        new("device.specs.gpu", "Supports graphics-performance recommendations without hardware serials."),
        new("device.specs.osDescription", "Provides only a coarse OS family for compatibility guidance."),
        new("device.specs.architecture", "Supports binary and platform compatibility guidance."),
        new("libraries[].index", "Links installed games to a device library without exposing its path."),
        new("libraries[].label", "Shows the user's library label in storage and curation views."),
        new("libraries[].gameCount", "Provides per-library summary counts."),
        new("libraries[].storage.kind", "Enables internal, SD-card, external, and network storage guidance."),
        new("libraries[].storage.freeBytes", "Enables install-size and free-space guidance."),
        new("libraries[].storage.totalBytes", "Provides storage-capacity context for recommendations."),
        new("games[].appId", "Provides the stable Steam application key used to resolve a game."),
        new("games[].name", "Provides the user's local title, including explicitly added non-Steam titles."),
        new("games[].installed", "Enables installed/backlog filtering per device."),
        new("games[].libraryIndex", "Associates an installed game with its path-free library record."),
        new("games[].installDir", "Provides the relative Steam install-folder name, never a full path."),
        new("games[].sizeOnDiskBytes", "Enables storage-fit and cleanup recommendations."),
        new("games[].playtimeMinutes", "Enables played/unplayed and taste-context recommendations."),
        new("games[].lastUpdated", "Supports freshness and recently-updated views."),
        new("games[].lastPlayed", "Supports recency-aware recommendations."),
        new("games[].categories[]", "Carries the user's Steam collection membership for filtering and taste context."),
        new("categories[].name", "Carries the user's Steam collection vocabulary."),
        new("categories[].gameCount", "Provides collection summary counts."),
        new("stats.libraryCount", "Provides a consistency and summary aggregate."),
        new("stats.installedGameCount", "Provides a consistency and summary aggregate."),
        new("stats.totalSizeOnDiskBytes", "Provides a device storage summary."),
        new("stats.scope", "Carries the producer-reported coverage marker; current observedSubset means partial evidence, while consumers normalize legacy false-full reports operationally."),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Projects a local snapshot into the explicit hosted field whitelist.</summary>
    public static HostedSnapshot Create(SnapshotDocument snapshot) =>
        Create(snapshot, Environment.MachineName);

    internal static HostedSnapshot Create(SnapshotDocument snapshot, string machineName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SnapshotSource source = snapshot.Source
            ?? throw new InvalidDataException("Snapshot source is required for hosted upload.");
        SnapshotDevice device = snapshot.Device
            ?? throw new InvalidDataException("Snapshot device is required for hosted upload.");
        SnapshotStats stats = snapshot.Stats
            ?? throw new InvalidDataException("Snapshot stats are required for hosted upload.");
        IReadOnlyList<SnapshotLibrary> libraries = snapshot.Libraries
            ?? throw new InvalidDataException("Snapshot libraries cannot be null for hosted upload.");
        IReadOnlyList<SnapshotGame> games = snapshot.Games
            ?? throw new InvalidDataException("Snapshot games cannot be null for hosted upload.");
        IReadOnlyList<SnapshotCategory> categories = snapshot.Categories
            ?? throw new InvalidDataException("Snapshot categories cannot be null for hosted upload.");

        RequireValue(snapshot.SchemaVersion, "schemaVersion");
        RequireValue(snapshot.SnapshotId, "snapshotId");
        RequireValue(source.Tool, "source.tool");
        RequireValue(source.ToolVersion, "source.toolVersion");
        RequireValue(device.Id, "device.id");

        return new HostedSnapshot
        {
            SchemaVersion = snapshot.SchemaVersion,
            SnapshotId = snapshot.SnapshotId,
            CreatedAt = snapshot.CreatedAt,
            Source = new HostedSnapshotSource
            {
                Tool = source.Tool,
                ToolVersion = source.ToolVersion,
                Platform = source.Platform,
            },
            Device = ProjectDevice(device, machineName),
            Libraries = libraries.Select(ProjectLibrary).ToArray(),
            Games = games.Select(ProjectGame).ToArray(),
            Categories = categories.Select(ProjectCategory).ToArray(),
            Stats = new HostedSnapshotStats
            {
                LibraryCount = stats.LibraryCount,
                InstalledGameCount = stats.InstalledGameCount,
                TotalSizeOnDiskBytes = stats.TotalSizeOnDiskBytes,
                Scope = stats.Scope,
            },
        };
    }

    /// <summary>Creates the canonical bytes shared by preview and upload.</summary>
    public static HostedUpload Prepare(SnapshotDocument snapshot) =>
        Prepare(snapshot, Environment.MachineName);

    internal static HostedUpload Prepare(SnapshotDocument snapshot, string machineName)
    {
        HostedSnapshot projected = Create(snapshot, machineName);
        return new HostedUpload(projected, Serialize(projected));
    }

    public static string Serialize(HostedSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    public static HostedSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<HostedSnapshot>(json, JsonOptions)
        ?? throw new InvalidDataException("Hosted snapshot JSON deserialized to null.");

    /// <summary>Reduces an exact OS build string to the family needed by hosted recommendations.</summary>
    public static string? CoarsenOsDescription(OsPlatform os, string? osDescription)
    {
        if (string.IsNullOrWhiteSpace(osDescription))
            return null;

        return os switch
        {
            OsPlatform.Windows => "Windows 10/11",
            OsPlatform.Linux => "Linux",
            OsPlatform.MacOs => "macOS",
            _ => "Unknown OS",
        };
    }

    private static HostedSnapshotDevice ProjectDevice(SnapshotDevice device, string machineName) => new()
    {
        Id = device.Id,
        Name = ProjectDeviceName(device.Name, machineName),
        Type = device.Type,
        Os = device.Os,
        Specs = ProjectSpecs(device.Os, device.Specs),
    };

    private static string ProjectDeviceName(string? name, string machineName)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name.Trim(), machineName, StringComparison.OrdinalIgnoreCase))
        {
            return DeviceIdentity.DefaultDeviceName;
        }

        return name.Trim();
    }

    private static HostedDeviceSpecs? ProjectSpecs(OsPlatform os, DeviceSpecs? specs)
    {
        if (specs is null)
            return null;

        var projected = new HostedDeviceSpecs
        {
            Cpu = specs.Cpu,
            LogicalCores = specs.LogicalCores,
            TotalMemoryBytes = specs.TotalMemoryBytes,
            Gpu = specs.Gpu,
            OsDescription = CoarsenOsDescription(os, specs.OsDescription),
            Architecture = specs.Architecture,
        };

        return projected.Cpu is null && projected.LogicalCores is null &&
            projected.TotalMemoryBytes is null && projected.Gpu is null &&
            projected.OsDescription is null && projected.Architecture is null
            ? null
            : projected;
    }

    private static HostedSnapshotLibrary ProjectLibrary(SnapshotLibrary library)
    {
        if (library is null)
            throw new InvalidDataException("Snapshot libraries cannot contain null entries.");
        RequireValue(library.Label, "libraries[].label");

        return new HostedSnapshotLibrary
        {
            Index = library.Index,
            Label = library.Label,
            GameCount = library.GameCount,
            Storage = library.Storage is null
                ? null
                : new HostedSnapshotStorage
                {
                    Kind = library.Storage.Kind,
                    FreeBytes = library.Storage.FreeBytes,
                    TotalBytes = library.Storage.TotalBytes,
                },
        };
    }

    private static HostedSnapshotGame ProjectGame(SnapshotGame game)
    {
        if (game is null)
            throw new InvalidDataException("Snapshot games cannot contain null entries.");
        RequireValue(game.Name, "games[].name");

        return new HostedSnapshotGame
        {
            AppId = game.AppId,
            Name = game.Name,
            Installed = game.Installed,
            LibraryIndex = game.LibraryIndex,
            InstallDir = game.InstallDir,
            SizeOnDiskBytes = game.SizeOnDiskBytes,
            PlaytimeMinutes = game.PlaytimeMinutes,
            LastUpdated = game.LastUpdated,
            LastPlayed = game.LastPlayed,
            Categories = game.Categories?.ToArray()
                ?? throw new InvalidDataException("Game categories cannot be null for hosted upload."),
        };
    }

    private static HostedSnapshotCategory ProjectCategory(SnapshotCategory category)
    {
        if (category is null)
            throw new InvalidDataException("Snapshot categories cannot contain null entries.");
        RequireValue(category.Name, "categories[].name");

        return new HostedSnapshotCategory { Name = category.Name, GameCount = category.GameCount };
    }

    private static void RequireValue(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Snapshot field '{path}' is required for hosted upload.");
    }
}
