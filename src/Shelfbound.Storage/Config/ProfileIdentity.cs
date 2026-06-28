namespace Shelfbound.Storage.Config;

/// <summary>
/// Resolves the current owner/profile id for stored data — the identity seam. Locally this is the
/// configured active profile, else the primary Steam account, else a fixed local id. The hosted layer
/// replaces this with an auth-backed resolver without changing the storage contract or data model.
/// </summary>
public static class ProfileIdentity
{
    public const string LocalFallback = "local";

    public static string Resolve(ShelfboundConfig config, string? primarySteamId64 = null) =>
        config.ActiveProfileId
        ?? (string.IsNullOrWhiteSpace(primarySteamId64) ? LocalFallback : primarySteamId64);
}
