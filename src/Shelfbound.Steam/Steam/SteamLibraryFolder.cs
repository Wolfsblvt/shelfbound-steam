namespace Shelfbound.Steam.Steam;

/// <summary>A Steam library folder discovered from libraryfolders.vdf.</summary>
public sealed record SteamLibraryFolder
{
    public required int Index { get; init; }
    public required string Path { get; init; }
    public required string Label { get; init; }

    /// <summary>App ids Steam records as installed in this library.</summary>
    public required IReadOnlyList<int> AppIds { get; init; }

    /// <summary>The steamapps directory for this library.</summary>
    public string SteamAppsPath => System.IO.Path.Combine(Path, "steamapps");
}
