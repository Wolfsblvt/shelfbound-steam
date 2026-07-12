using Shelfbound.Steam.Vdf;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Parses userdata/&lt;id&gt;/7/remote/sharedconfig.vdf into the user's local collections:
/// app id -> ordered category (tag) names. This is Steam's legacy category store; modern dynamic
/// collections (kept in the client's leveldb) are not read yet. See docs/project/snapshot-schema.md.
/// </summary>
public static class SharedConfigParser
{
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Parse(string vdfText)
    {
        return Parse(VdfParser.Parse(vdfText));
    }

    public static IReadOnlyDictionary<int, IReadOnlyList<string>> ParseFile(string path) =>
        Parse(VdfParser.ParseFile(path));

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> Parse(VdfObject root)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();

        VdfObject? apps = root
            .GetObject("UserRoamingConfigStore")
            ?.GetObject("Software")
            ?.GetObject("Valve")
            ?.GetObject("Steam")
            ?.GetObject("apps");
        if (apps is null)
            return result;

        foreach (var (appKey, appObj) in apps.Objects)
        {
            if (!int.TryParse(appKey, out int appId))
                continue;

            VdfObject? tags = appObj.GetObject("tags");
            if (tags is null || tags.Values.Count == 0)
                continue;

            // Tags are keyed by numeric index ("0", "1", ...); preserve that order.
            var categories = tags.Values
                .OrderBy(kv => int.TryParse(kv.Key, out int n) ? n : int.MaxValue)
                .Select(kv => kv.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (categories.Count > 0)
                result[appId] = categories;
        }

        return result;
    }
}
