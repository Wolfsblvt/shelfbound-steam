namespace Shelfbound.Core.Model;

/// <summary>
/// Lightweight, best-effort hardware facts about a device, used for device-aware recommendations
/// ("runs well on this machine", "too heavy for the Deck") and capability-based search. Every field is
/// optional — collection is defensive and may yield nulls. These carry no identifiers or serials,
/// but model combinations can still be distinctive and must be disclosed. Hosted upload coarsens the
/// exact OS build. See docs/project/privacy-and-data.md.
/// </summary>
public sealed record DeviceSpecs
{
    /// <summary>CPU description (best-effort; e.g. the vendor identifier or model name).</summary>
    public string? Cpu { get; init; }

    public int? LogicalCores { get; init; }

    public long? TotalMemoryBytes { get; init; }

    /// <summary>GPU name, when it can be determined (best-effort; often null for now).</summary>
    public string? Gpu { get; init; }

    /// <summary>OS description string (e.g. "Microsoft Windows 10.0.26200").</summary>
    public string? OsDescription { get; init; }

    /// <summary>CPU/OS architecture (e.g. X64, Arm64).</summary>
    public string? Architecture { get; init; }
}
