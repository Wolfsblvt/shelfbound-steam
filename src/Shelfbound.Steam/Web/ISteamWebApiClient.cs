namespace Shelfbound.Steam.Web;

/// <summary>
/// Abstraction over the Steam Web API so the source can be swapped, mocked, or rate-limited.
/// Keep external providers behind interfaces (see docs/project/integrations.md philosophy).
/// </summary>
public interface ISteamWebApiClient
{
    /// <summary>Fetches the owned games (with total playtime) for a public/visible Steam profile.</summary>
    Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(
        string steamId64, string apiKey, CancellationToken cancellationToken = default);
}
