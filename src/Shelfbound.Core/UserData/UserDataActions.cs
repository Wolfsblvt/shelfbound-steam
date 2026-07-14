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
    /// Records conservative first-observation timestamps. The first call establishes the baseline.
    /// Later observations are dated at <paramref name="now"/> only under a stable
    /// <see cref="LibraryScope.FullLibrary"/> source; partial installed/observed-subset scans cannot prove
    /// acquisition, and a scope expansion only reveals games that may already have been present. Those
    /// observations are stamped at the baseline so UI and MCP consumers do not call them newly bought.
    /// Pure — call inside a store transaction.
    /// </summary>
    public static void RecordFirstSeen(UserProfile profile, IEnumerable<int> appIds, DateTimeOffset now, LibraryScope scanScope)
    {
        bool firstScan = profile.FirstScanAt is null;
        profile.FirstScanAt ??= now;

        bool scopeExpanded = !firstScan &&
            LibraryScopeSemantics.IsBroaderThan(scanScope, profile.WidestScanScope);
        bool canEstablishAcquisition = !firstScan && !scopeExpanded &&
            LibraryScopeSemantics.IsComplete(scanScope);
        DateTimeOffset firstSeenAt = canEstablishAcquisition ? now : profile.FirstScanAt.Value;

        foreach (int appId in appIds)
            profile.FirstSeen.TryAdd(appId, firstSeenAt);

        profile.WidestScanScope = LibraryScopeSemantics.BroaderOf(profile.WidestScanScope, scanScope);
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
