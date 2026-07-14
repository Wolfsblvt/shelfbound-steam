namespace Shelfbound.Core.Model;

/// <summary>Operating system family a snapshot was produced on.</summary>
public enum OsPlatform
{
    Unknown = 0,
    Windows,
    Linux,
    MacOs,
}

/// <summary>
/// Kind of device a snapshot represents. Steam Deck is intentionally distinct from generic Linux
/// because install location (internal SSD vs SD card) and usage differ in ways that matter.
/// </summary>
public enum DeviceType
{
    Unknown = 0,
    Desktop,
    Laptop,
    SteamDeck,
    Server,
}

/// <summary>
/// The kind of storage medium a Steam library sits on. Enables device-aware views ("fits on your SD
/// card", "free up space") without ever exposing a filesystem path. Producers emit <see cref="Unknown"/>
/// when the OS can't tell the medium apart (e.g. SD-vs-USB on a desktop card reader) — they never guess.
/// </summary>
public enum StorageKind
{
    Unknown = 0,

    /// <summary>Internal fixed drive (the machine's built-in SSD/HDD).</summary>
    Internal,

    /// <summary>Removable SD/microSD/eMMC card — the Steam Deck's expansion slot.</summary>
    SdCard,

    /// <summary>External/removable drive (e.g. a USB SSD or stick).</summary>
    External,

    /// <summary>Network-mounted storage (NFS/SMB and similar).</summary>
    Network,
}

/// <summary>
/// How much of a game library a snapshot observes. Scope describes coverage, not ownership or access.
/// Consumers MUST treat <see cref="InstalledOnly"/> and <see cref="ObservedSubset"/> as partial:
/// absence from either scope proves nothing about ownership, access, or acquisition.
/// Enum ordinals are published compatibility values, not a coverage ordering. Use
/// <see cref="LibraryScopeSemantics"/> for comparisons.
/// </summary>
public enum LibraryScope
{
    /// <summary>Only games observed installed on this device are included.</summary>
    InstalledOnly = 0,

    /// <summary>
    /// A source with an explicit completeness contract supplied the complete game list. This published
    /// value remains 1 for compatibility; current Steam Web API enrichment does not qualify.
    /// </summary>
    FullLibrary = 1,

    /// <summary>
    /// Positive observations beyond locally installed games are included, but the source does not
    /// guarantee completeness. Missing games prove nothing.
    /// </summary>
    ObservedSubset = 2,
}
