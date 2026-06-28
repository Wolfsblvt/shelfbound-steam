using System.Runtime.InteropServices;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Locates the active Steam installation root. Resolution order:
/// explicit override, SHELFBOUND_STEAM_PATH env var, then well-known per-OS install locations.
/// Reading the Windows registry (HKCU\Software\Valve\Steam\SteamPath) to support non-default
/// Windows installs is a planned enhancement; see docs/project/DECISIONS.md.
/// </summary>
public static class SteamInstallLocator
{
    public static string? Locate(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return IsSteamRoot(overridePath) ? overridePath : null;

        string? env = Environment.GetEnvironmentVariable("SHELFBOUND_STEAM_PATH");
        if (!string.IsNullOrWhiteSpace(env) && IsSteamRoot(env))
            return env;

        foreach (var candidate in CandidatePaths())
        {
            if (IsSteamRoot(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>True if the directory looks like a Steam root (contains a steamapps folder).</summary>
    public static bool IsSteamRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        return Directory.Exists(Path.Combine(path, "steamapps"));
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { } pf86)
                yield return Path.Combine(pf86, "Steam");
            if (Environment.GetEnvironmentVariable("ProgramFiles") is { } pf)
                yield return Path.Combine(pf, "Steam");
            yield return @"C:\Steam";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
        else // Linux / SteamOS (Steam Deck)
        {
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".steam", "root");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        }
    }
}
