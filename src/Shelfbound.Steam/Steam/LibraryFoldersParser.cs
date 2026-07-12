using Shelfbound.Steam.Vdf;

namespace Shelfbound.Steam.Steam;

/// <summary>Parses steamapps/libraryfolders.vdf into the set of Steam library folders.</summary>
public static class LibraryFoldersParser
{
    public static IReadOnlyList<SteamLibraryFolder> Parse(string vdfText)
    {
        return Parse(VdfParser.Parse(vdfText));
    }

    public static IReadOnlyList<SteamLibraryFolder> ParseFile(string path) => Parse(VdfParser.ParseFile(path));

    private static IReadOnlyList<SteamLibraryFolder> Parse(VdfObject root)
    {
        var container = root.GetObject("libraryfolders")
            ?? throw new FormatException("libraryfolders.vdf is missing its 'libraryfolders' root object.");

        var folders = new List<SteamLibraryFolder>();
        foreach (var (key, folder) in container.Objects)
        {
            // Library entries are keyed by a numeric index; skip anything else defensively.
            if (!int.TryParse(key, out int index))
                continue;

            string path = folder.GetValue("path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var appIds = new List<int>();
            if (folder.GetObject("apps") is { } apps)
            {
                foreach (var appKey in apps.Values.Keys)
                {
                    if (int.TryParse(appKey, out int appId))
                        appIds.Add(appId);
                }
            }

            string label = folder.GetValue("label") ?? string.Empty;
            folders.Add(new SteamLibraryFolder
            {
                Index = index,
                Path = path,
                Label = string.IsNullOrWhiteSpace(label) ? $"library-{index}" : label,
                AppIds = appIds,
            });
        }

        return folders.OrderBy(f => f.Index).ToList();
    }
}
