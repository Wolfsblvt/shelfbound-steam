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
    /// time; later calls only timestamp apps not seen before, so games first seen after the baseline
    /// are the genuinely new ones. Pure — call inside a store transaction.
    /// </summary>
    public static void RecordFirstSeen(UserProfile profile, IEnumerable<int> appIds, DateTimeOffset now)
    {
        profile.FirstScanAt ??= now;
        foreach (int appId in appIds)
            profile.FirstSeen.TryAdd(appId, now);
    }
}
