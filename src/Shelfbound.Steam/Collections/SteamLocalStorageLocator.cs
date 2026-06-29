namespace Shelfbound.Steam.Collections;

/// <summary>
/// Locates the Steam client's Chromium "Local Storage" LevelDB directory, where modern collections live.
/// This is the desktop client's web cache — <b>not</b> under the Steam install root. Windows is the
/// validated path; Linux/macOS are best-effort candidates (a miss just falls back to legacy categories).
/// Override with <c>SHELFBOUND_STEAM_LOCALSTORAGE</c> for non-standard installs or testing.
/// </summary>
internal static class SteamLocalStorageLocator
{
    public const string OverrideEnvVar = "SHELFBOUND_STEAM_LOCALSTORAGE";

    public static string? Locate()
    {
        string? overridePath = Environment.GetEnvironmentVariable(OverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Directory.Exists(overridePath) ? overridePath : null;

        foreach (string candidate in CandidatePaths())
            if (Directory.Exists(candidate))
                return candidate;
        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        const string tail = "htmlcache/Local Storage/leveldb";

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Combine(localAppData, "Steam", tail);
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS())
            {
                yield return Combine(home, "Library/Application Support/Steam/config", tail);
            }
            else // Linux and other Unix
            {
                yield return Combine(home, ".local/share/Steam/config", tail);
                yield return Combine(home, ".steam/steam/config", tail);
            }
        }
    }

    private static string Combine(params string[] parts) =>
        Path.Combine(parts.SelectMany(p => p.Split('/', '\\')).ToArray());
}
