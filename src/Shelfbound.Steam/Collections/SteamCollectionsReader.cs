using System.Text;
using System.Text.Json;

namespace Shelfbound.Steam.Collections;

/// <summary>
/// Reads a user's <b>modern</b> Steam collections (categories) from the desktop client's Chromium Local
/// Storage LevelDB and returns them in the same <c>appId → ordered category names</c> shape the legacy
/// <see cref="Steam.SharedConfigParser"/> produces — so the scanner can prefer modern collections and
/// fall back to the (often stale) legacy <c>sharedconfig.vdf</c>. See docs/project/steam-collections.md.
///
/// <para>v1 reads only static collections (explicit <c>added</c> lists). Dynamic, rule-based
/// (<c>filterSpec</c>) collections are skipped — their membership can't be computed from the stored data
/// alone. Best-effort: returns null when the store/key is absent so the caller can fall back.</para>
/// </summary>
internal static class SteamCollectionsReader
{
    /// <summary>
    /// Reads modern collections for <paramref name="accountId"/> (the 32-bit Steam account id, i.e. the
    /// <c>userdata/&lt;id&gt;</c> folder name), or null if the store/key is unavailable. Throws only on a
    /// genuinely corrupt namespace value, which the caller treats as "fall back to legacy".
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>>? TryRead(long accountId, string? leveldbDir = null)
    {
        leveldbDir ??= SteamLocalStorageLocator.Locate();
        if (leveldbDir is null || !Directory.Exists(leveldbDir))
            return null;

        byte[] namespaceKey = BuildNamespaceKey(accountId);
        byte[]? raw = ChromiumLevelDb.ReadLatestValue(leveldbDir, namespaceKey);
        if (raw is null || raw.Length == 0)
            return null;

        return ParseNamespaceJson(DecodeLocalStorageValue(raw));
    }

    /// <summary>
    /// Parses the <c>cloud-storage-namespace-1</c> JSON (an array of <c>[entryKey, {value, …}]</c> pairs)
    /// into <c>appId → ordered category names</c>. Internal so it can be unit-tested without a LevelDB.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>>? ParseNamespaceJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > SteamInputLimits.MaxNamespaceJsonChars)
            throw new InvalidDataException(
                $"Steam collections JSON exceeds the {SteamInputLimits.MaxNamespaceJsonChars}-character limit.");

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        // Preserve the order collections appear in (Steam's own ordering) for each game's category list.
        var byApp = new Dictionary<int, List<string>>();

        int collectionEntries = 0;
        int memberships = 0;
        foreach (JsonElement pair in document.RootElement.EnumerateArray())
        {
            if (++collectionEntries > SteamInputLimits.MaxCollectionEntries)
                throw new InvalidDataException("Steam collections entry count exceeds the supported limit.");
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2)
                continue;
            if (pair[0].ValueKind != JsonValueKind.String ||
                !pair[0].GetString()!.StartsWith("user-collections.", StringComparison.Ordinal))
                continue;
            if (!pair[1].TryGetProperty("value", out JsonElement valueProp) ||
                valueProp.ValueKind != JsonValueKind.String)
                continue;

            AddCollection(valueProp.GetString()!, byApp, ref memberships);
        }

        return byApp.Count == 0
            ? null
            : byApp.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    private static void AddCollection(
        string collectionJson,
        Dictionary<int, List<string>> byApp,
        ref int memberships)
    {
        JsonElement collection;
        try
        {
            using var doc = JsonDocument.Parse(collectionJson);
            collection = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return; // a single malformed collection shouldn't sink the rest
        }

        // Skip dynamic collections — their membership is rule-based, not the stored 'added' list.
        if (collection.TryGetProperty("filterSpec", out JsonElement filter) &&
            filter.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            return;

        if (!collection.TryGetProperty("name", out JsonElement nameProp) ||
            nameProp.ValueKind != JsonValueKind.String)
            return;
        string name = nameProp.GetString()!;
        if (string.IsNullOrWhiteSpace(name) || name.Length > SteamInputLimits.MaxCategoryNameChars)
            return;

        if (!collection.TryGetProperty("added", out JsonElement added) || added.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement app in added.EnumerateArray())
        {
            if (++memberships > SteamInputLimits.MaxCollectionMemberships)
                throw new InvalidDataException("Steam collection membership count exceeds the supported limit.");
            if (app.ValueKind != JsonValueKind.Number || !app.TryGetInt32(out int appId))
                continue;
            if (!byApp.TryGetValue(appId, out List<string>? names))
                byApp[appId] = names = [];
            if (!names.Contains(name))
                names.Add(name);
        }
    }

    /// <summary>The Chromium LocalStorage key for a user's collections namespace.</summary>
    private static byte[] BuildNamespaceKey(long accountId)
    {
        // _<origin>\x00\x01U<accountId>-cloud-storage-namespace-1
        byte[] prefix = Encoding.ASCII.GetBytes("_https://steamloopback.host");
        byte[] suffix = Encoding.ASCII.GetBytes($"U{accountId}-cloud-storage-namespace-1");
        var key = new byte[prefix.Length + 2 + suffix.Length];
        prefix.CopyTo(key, 0);
        key[prefix.Length] = 0x00;
        key[prefix.Length + 1] = 0x01;
        suffix.CopyTo(key, prefix.Length + 2);
        return key;
    }

    /// <summary>Decodes a Chromium LocalStorage value: a 1-byte encoding marker then the payload.</summary>
    private static string DecodeLocalStorageValue(byte[] raw) =>
        raw[0] == 0x00 // 0x00 = UTF-16LE, 0x01 = Latin-1
            ? Encoding.Unicode.GetString(raw, 1, raw.Length - 1)
            : Encoding.Latin1.GetString(raw, 1, raw.Length - 1);
}
