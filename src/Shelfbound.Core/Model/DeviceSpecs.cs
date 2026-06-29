namespace Shelfbound.Core.Model;

/// <summary>
/// Lightweight, best-effort hardware facts about a device, used for device-aware recommendations
/// ("runs well on this machine", "too heavy for the Deck") and capability-based search. Every field is
/// optional — collection is defensive and may yield nulls. These are device facts, not personal data,
/// and carry no identifiers/serials/fingerprints. See docs/project/privacy-and-data.md.
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
