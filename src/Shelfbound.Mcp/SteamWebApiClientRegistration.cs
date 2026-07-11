using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shelfbound.Steam.Web;

namespace Shelfbound.Mcp;

/// <summary>
/// Registers the Steam Web API client with a stable name so its framework HTTP logging can be scoped
/// without affecting other MCP logging categories.
/// </summary>
internal static class SteamWebApiClientRegistration
{
    public const string Name = "SteamWebApi";

    /// <summary>Registers the typed Steam Web API client on the named factory pipeline.</summary>
    public static IHttpClientBuilder AddSteamWebApiClient(this IServiceCollection services) =>
        services.AddHttpClient(Name).AddTypedClient<ISteamWebApiClient, SteamWebApiClient>();

    /// <summary>
    /// Suppresses request-URI-bearing framework Information logs for the Steam client only. The stderr
    /// sink still masks any sensitive query value that reaches a warning or a future logging path.
    /// </summary>
    public static ILoggingBuilder AddSteamWebApiLogging(this ILoggingBuilder logging) =>
        logging.AddFilter($"System.Net.Http.HttpClient.{Name}", LogLevel.Warning);
}
