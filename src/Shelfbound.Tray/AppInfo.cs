using System.Reflection;

namespace Shelfbound.Tray;

/// <summary>
/// Static app identity: the running version (from the assembly) and the public GitHub URLs the UI links to.
/// Single source so the repo URL / version aren't duplicated across the update service, snapshot builder, UI.
/// </summary>
public static class AppInfo
{
    /// <summary>The public repo that hosts the tray's releases and source.</summary>
    public const string RepoUrl = "https://github.com/Wolfsblvt/shelfbound-steam";

    /// <summary>The tray's version (e.g. "0.6.0"), parsed from the assembly's informational version.</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>The GitHub Releases listing.</summary>
    public static string ReleasesUrl => $"{RepoUrl}/releases";

    /// <summary>The GitHub Release page for a specific tray version (its notes come from the changelog).</summary>
    public static string ReleaseUrl(string version) => $"{RepoUrl}/releases/tag/tray-v{version}";

    private static string ResolveVersion()
    {
        string? raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(raw))
            return "0.0.0";
        int plus = raw.IndexOf('+'); // strip the "+<git-sha>" build metadata suffix, if present
        return plus >= 0 ? raw[..plus] : raw;
    }
}
