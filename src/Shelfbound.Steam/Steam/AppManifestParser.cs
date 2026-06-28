using Shelfbound.Steam.Vdf;

namespace Shelfbound.Steam.Steam;

/// <summary>Parses a single appmanifest_*.acf file.</summary>
public static class AppManifestParser
{
    public static AppManifest Parse(string vdfText)
    {
        var root = VdfParser.Parse(vdfText);
        var state = root.GetObject("AppState")
            ?? throw new FormatException("appmanifest is missing its 'AppState' root object.");

        int appId = ParseInt(state.GetValue("appid")) ?? 0;
        return new AppManifest
        {
            AppId = appId,
            Name = state.GetValue("name") ?? $"App {appId}",
            StateFlags = ParseInt(state.GetValue("StateFlags")) ?? 0,
            InstallDir = state.GetValue("installdir"),
            SizeOnDisk = ParseLong(state.GetValue("SizeOnDisk")),
            LastUpdated = ParseUnixSeconds(state.GetValue("LastUpdated")),
            LastPlayed = ParseUnixSeconds(state.GetValue("LastPlayed")),
        };
    }

    public static AppManifest ParseFile(string path) => Parse(File.ReadAllText(path));

    private static int? ParseInt(string? s) => int.TryParse(s, out var v) ? v : null;

    private static long? ParseLong(string? s) => long.TryParse(s, out var v) ? v : null;

    private static DateTimeOffset? ParseUnixSeconds(string? s)
    {
        if (!long.TryParse(s, out long seconds) || seconds <= 0)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
