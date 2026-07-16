using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Locates the active Steam installation root. Resolution order:
/// explicit override, SHELFBOUND_STEAM_PATH env var, the Windows current-user registry, then well-known
/// per-OS install locations.
/// </summary>
public static class SteamInstallLocator
{
    private static readonly SteamInstallLocatorSources SystemSources = new(
        Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows,
        ReadWindowsRegistrySteamPath,
        CandidatePaths);

    public static string? Locate(string? overridePath = null)
        => Locate(overridePath, SystemSources);

    internal static string? Locate(string? overridePath, SteamInstallLocatorSources sources)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return IsSteamRoot(overridePath) ? overridePath : null;

        string? env = sources.GetEnvironmentVariable("SHELFBOUND_STEAM_PATH");
        if (!string.IsNullOrWhiteSpace(env) && IsSteamRoot(env))
            return env;

        if (sources.IsWindows())
        {
            string? registryPath = TryReadRegistryPath(sources.GetWindowsRegistrySteamPath);
            if (!string.IsNullOrWhiteSpace(registryPath) && IsSteamRoot(registryPath))
                return registryPath;
        }

        foreach (var candidate in sources.GetCandidatePaths())
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

    private static object? ReadWindowsRegistrySteamPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using RegistryKey? steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
        return steamKey?.GetValue("SteamPath");
    }

    private static string? TryReadRegistryPath(Func<object?> readRegistrySteamPath)
    {
        try
        {
            return readRegistrySteamPath() as string;
        }
        catch (Exception exception) when (exception is IOException
                                         or ObjectDisposedException
                                         or PlatformNotSupportedException
                                         or SecurityException
                                         or UnauthorizedAccessException)
        {
            return null;
        }
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

internal sealed record SteamInstallLocatorSources(
    Func<string, string?> GetEnvironmentVariable,
    Func<bool> IsWindows,
    Func<object?> GetWindowsRegistrySteamPath,
    Func<IEnumerable<string>> GetCandidatePaths);
