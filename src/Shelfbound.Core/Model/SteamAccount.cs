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
}
