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
/// How complete a snapshot's game list is. A local scan only sees the games installed on this device;
/// the full owned library (including owned-but-not-installed games) requires Steam Web API enrichment.
/// Consumers MUST treat <see cref="InstalledOnly"/> as "absence is not proof of non-ownership": a game
/// missing from such a snapshot may simply be owned-but-not-installed, not un-owned.
/// </summary>
public enum LibraryScope
{
    /// <summary>Only games installed on this device are included (no Steam Web API enrichment ran).</summary>
    InstalledOnly = 0,

    /// <summary>The full owned library, including owned-but-not-installed games, is included.</summary>
    FullLibrary,
}
