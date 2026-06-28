namespace Shelfbound.Storage.Config;

/// <summary>Well-known local paths for Shelfbound config and data, under the user's config directory.</summary>
public static class ShelfboundPaths
{
    /// <summary>e.g. %APPDATA%\Shelfbound on Windows, ~/.config/Shelfbound on Linux/macOS.</summary>
    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shelfbound");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");
    public static string ProfilesDirectory => Path.Combine(ConfigDirectory, "profiles");
    public static string DeviceIdFile => Path.Combine(ConfigDirectory, "device-id");
}
