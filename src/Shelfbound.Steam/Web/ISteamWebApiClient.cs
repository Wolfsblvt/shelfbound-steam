namespace Shelfbound.Steam.Web;

/// <summary>
/// Abstraction over the Steam Web API so the source can be swapped, mocked, or rate-limited.
/// Keep external providers behind interfaces (see docs/project/integrations.md philosophy).
/// </summary>
public interface ISteamWebApiClient
{
    /// <summary>
    /// Fetches positive owned-game/playtime observations for a visible Steam profile. The result
    /// distinguishes a usable non-empty response from missing, empty, or malformed evidence.
    /// </summary>
    Task<OwnedGamesResult> GetOwnedGamesAsync(
        string steamId64, string apiKey, CancellationToken cancellationToken = default);
}
