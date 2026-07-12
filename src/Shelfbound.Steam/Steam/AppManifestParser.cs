using Shelfbound.Steam.Vdf;

namespace Shelfbound.Steam.Steam;

/// <summary>Parses a single appmanifest_*.acf file.</summary>
public static class AppManifestParser
{
    public static AppManifest Parse(string vdfText)
    {
        return Parse(VdfParser.Parse(vdfText));
    }

    public static AppManifest ParseFile(string path) => Parse(VdfParser.ParseFile(path));

    private static AppManifest Parse(VdfObject root)
    {
        var state = root.GetObject("AppState")
            ?? throw new FormatException("appmanifest is missing its 'AppState' root object.");

        int appId = ParseInt(state.GetValue("appid")) ?? 0;
        return new AppManifest
        {
            AppId = appId,
            Name = state.GetValue("name") ?? $"App {appId}",
            StateFlags = ParseInt(state.GetValue("StateFlags")) ?? 0,
            InstallDir = NormalizeInstallDirectory(state.GetValue("installdir")),
            SizeOnDisk = ParseLong(state.GetValue("SizeOnDisk")),
            LastUpdated = ParseUnixSeconds(state.GetValue("LastUpdated")),
            LastPlayed = ParseUnixSeconds(state.GetValue("LastPlayed")),
        };
    }

    private static int? ParseInt(string? s) => int.TryParse(s, out var v) ? v : null;

    private static long? ParseLong(string? s) => long.TryParse(s, out var v) ? v : null;

    private static DateTimeOffset? ParseUnixSeconds(string? s)
    {
        if (!long.TryParse(s, out long seconds) || seconds <= 0)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static string? NormalizeInstallDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > SteamInputLimits.MaxInstallDirectoryNameChars ||
            value is "." or ".." ||
            value.IndexOfAny(['/', '\\', '\0']) >= 0 ||
            (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':') ||
            Path.IsPathRooted(value))
        {
            return null;
        }

        return value;
    }
}
