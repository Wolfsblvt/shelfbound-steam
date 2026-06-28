using System.Reflection;
using Microsoft.Extensions.Logging;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Enrichment;
using Shelfbound.Steam.Steam;
using Shelfbound.Steam.Web;

namespace Shelfbound.Mcp;

/// <summary>
/// Loads and holds the library snapshot for the MCP server. Configured via environment variables so an
/// MCP client (e.g. Claude Desktop) can set them in its server config:
/// <list type="bullet">
///   <item><c>SHELFBOUND_SNAPSHOT</c> — path to an existing snapshot JSON to load (skips scanning).</item>
///   <item><c>SHELFBOUND_STEAM_PATH</c> — Steam install root (else auto-detected).</item>
///   <item><c>STEAM_WEB_API_KEY</c> — if set, enriches with owned-but-not-installed games + playtime.</item>
/// </list>
/// </summary>
public sealed class SnapshotContext(ISteamWebApiClient steamWebApiClient, ILogger<SnapshotContext> logger)
{
    private SnapshotDocument? _snapshot;

    public SnapshotDocument Snapshot =>
        _snapshot ?? throw new InvalidOperationException("Snapshot has not been initialized.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string? snapshotFile = Environment.GetEnvironmentVariable("SHELFBOUND_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(snapshotFile) && File.Exists(snapshotFile))
        {
            _snapshot = SnapshotSerializer.Deserialize(await File.ReadAllTextAsync(snapshotFile, cancellationToken));
            logger.LogInformation("Loaded snapshot from {File} ({Games} games).", snapshotFile, _snapshot.Games.Count);
            return;
        }

        string? steamRoot = SteamInstallLocator.Locate(Environment.GetEnvironmentVariable("SHELFBOUND_STEAM_PATH"));
        if (steamRoot is null)
        {
            throw new InvalidOperationException(
                "Could not find a Steam installation. Set SHELFBOUND_STEAM_PATH, or SHELFBOUND_SNAPSHOT to a snapshot file.");
        }

        var device = new SnapshotDevice
        {
            Id = "mcp",
            Name = Environment.MachineName,
            Type = DeviceType.Unknown,
            Os = DetectOs(),
        };

        ScanResult scan = new SteamScanner().Scan(new SteamScanRequest
        {
            SteamRootPath = steamRoot,
            Device = device,
            ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.3.0",
        });
        SnapshotDocument snapshot = scan.Snapshot;

        string? apiKey = Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");
        SteamAccount? account = snapshot.SteamAccounts.FirstOrDefault(a => a.MostRecent)
            ?? snapshot.SteamAccounts.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey) && account is not null)
        {
            try
            {
                var owned = await steamWebApiClient.GetOwnedGamesAsync(account.SteamId64, apiKey, cancellationToken);
                snapshot = SteamWebEnricher.Enrich(snapshot, scan.CategoriesByApp, owned);
                logger.LogInformation("Enriched with {Owned} owned games from the Steam Web API.", owned.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Steam Web API enrichment failed; continuing with local data only.");
            }
        }

        _snapshot = snapshot;
        logger.LogInformation("Snapshot ready: {Games} games, {Installed} installed, {Categories} categories.",
            snapshot.Games.Count, snapshot.Stats.InstalledGameCount, snapshot.Categories.Count);
    }

    private static OsPlatform DetectOs()
    {
        if (OperatingSystem.IsWindows()) return OsPlatform.Windows;
        if (OperatingSystem.IsMacOS()) return OsPlatform.MacOs;
        if (OperatingSystem.IsLinux()) return OsPlatform.Linux;
        return OsPlatform.Unknown;
    }
}
