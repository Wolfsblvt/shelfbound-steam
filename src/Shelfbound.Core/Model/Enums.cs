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
/// How complete a snapshot's game list is. A local scan only sees the games installed on this device;
/// the full owned library (including owned-but-not-installed games) requires Steam Web API enrichment.
/// Consumers MUST treat <see cref="InstalledOnly"/> as "absence is not proof of non-ownership": a game
/// missing from such a snapshot may simply be owned-but-not-installed, not un-owned.
/// The values are ordered by increasing coverage, and recency baselining relies on that ordering to
/// detect a scope expansion (see <c>UserDataActions.RecordFirstSeen</c>) — keep it monotonic.
/// </summary>
public enum LibraryScope
{
    /// <summary>Only games installed on this device are included (no Steam Web API enrichment ran).</summary>
    InstalledOnly = 0,

    /// <summary>The full owned library, including owned-but-not-installed games, is included.</summary>
    FullLibrary,
}
