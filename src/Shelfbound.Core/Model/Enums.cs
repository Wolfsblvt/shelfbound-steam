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
