namespace Shelfbound.Core.Model;

/// <summary>A Steam account discovered in the local Steam client configuration.</summary>
public sealed record SteamAccount
{
    /// <summary>64-bit SteamID (a public identifier).</summary>
    public required string SteamId64 { get; init; }

    /// <summary>Local Steam login name. Optional; redacted before upload in cloud mode.</summary>
    public string? AccountName { get; init; }

    /// <summary>Public Steam display name.</summary>
    public string? PersonaName { get; init; }

    /// <summary>True if this was the most recently logged-in account on this device.</summary>
    public bool MostRecent { get; init; }

    /// <summary>
    /// 32-bit Steam account id — the <c>userdata/&lt;id&gt;</c> folder name — derived from
    /// <see cref="SteamId64"/>, or null if it can't be parsed.
    /// </summary>
    public long? AccountId => long.TryParse(SteamId64, out long id) ? id - SteamId64Base : null;

    private const long SteamId64Base = 76561197960265728L;
}
