using Shelfbound.Core.Model;

namespace Shelfbound.Core.UserData;

/// <summary>
/// Centralized mutations on a <see cref="UserProfile"/> so the CLI, MCP tools, and (later) the hosted
/// layer apply changes consistently (timestamps, upserts). Pure helpers; call them inside a store
/// transaction (e.g. <c>IUserDataStore.Update</c>).
/// </summary>
public static class UserDataActions
{
    /// <summary>Upserts a game's structured data via a transform over the existing (or new) record.</summary>
    public static GameUserData UpsertGame(UserProfile profile, int appId, Func<GameUserData, GameUserData> mutate)
    {
        var now = DateTimeOffset.UtcNow;
        GameUserData existing = profile.Games.TryGetValue(appId, out var current)
            ? current
            : new GameUserData { AppId = appId, CreatedAt = now, UpdatedAt = now };

        GameUserData updated = mutate(existing) with { AppId = appId, UpdatedAt = now };
        profile.Games[appId] = updated;
        return updated;
    }

    public static Memory AddMemory(UserProfile profile, MemoryScope scope, string? subject, string text,
        string source, string? evidence, MemoryConfidence confidence, bool userConfirmed)
    {
        var memory = new Memory
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text,
            Scope = scope,
            Subject = subject,
            Source = source,
            Evidence = evidence,
            Confidence = confidence,
            UserConfirmed = userConfirmed,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        profile.Memories.Add(memory);
        return memory;
    }

    public static CategoryDefinition SetCategoryDefinition(UserProfile profile, string name, string meaning)
    {
        var definition = new CategoryDefinition { Name = name, Meaning = meaning, UpdatedAt = DateTimeOffset.UtcNow };
        profile.CategoryDefinitions[name] = definition;
        return definition;
    }

    /// <summary>
    /// Records the first time each app id was observed owned, the proxy Shelfbound uses for "recently
    /// added/bought" (Steam exposes no purchase date). The first call establishes the baseline scan
    /// time; later calls only timestamp apps not seen before. A scan whose <paramref name="scanScope"/>
    /// is broader than any prior scan (e.g. installedOnly → fullLibrary) makes previously-owned games
    /// newly <em>visible</em>, not newly <em>added</em> — and Steam gives no way to tell a real purchase
    /// from a scope reveal — so those newly-seen apps are stamped at the baseline (not "now"), keeping
    /// them out of "recently added". Apps first seen under a stable-or-narrower scope are genuine
    /// acquisitions and get the current timestamp. Pure — call inside a store transaction.
    /// </summary>
    public static void RecordFirstSeen(UserProfile profile, IEnumerable<int> appIds, DateTimeOffset now, LibraryScope scanScope)
    {
        bool firstScan = profile.FirstScanAt is null;
        profile.FirstScanAt ??= now;

        // Coverage that exceeds anything seen before reveals already-owned games as if brand new. We
        // can't distinguish those from real purchases, so we conservatively baseline them (stamp at the
        // baseline scan time) rather than risk a whole library reading "Added N days ago". LibraryScope
        // is ordered by increasing coverage, so ">" means "strictly broader". Never on the first scan —
        // that IS the baseline: everything is stamped at "now" and reads as pre-existing.
        bool scopeExpanded = !firstScan && scanScope > profile.WidestScanScope;
        DateTimeOffset firstSeenAt = scopeExpanded ? profile.FirstScanAt.Value : now;

        foreach (int appId in appIds)
            profile.FirstSeen.TryAdd(appId, firstSeenAt);

        if (scanScope > profile.WidestScanScope)
            profile.WidestScanScope = scanScope;
    }

    /// <summary>
    /// Clears the "recently added" baseline — the scan time, every per-app first-seen stamp, and the
    /// widest-observed scope — so the next scan re-establishes it from the current library (everything
    /// then reads as pre-existing, and only later additions count as new). The recovery path for a
    /// profile whose baseline was skewed by a scope change before this hardening existed. Pure — call
    /// inside a store transaction.
    /// </summary>
    public static void ResetRecencyBaseline(UserProfile profile)
    {
        profile.FirstScanAt = null;
        profile.FirstSeen.Clear();
        profile.WidestScanScope = LibraryScope.InstalledOnly;
    }
}
