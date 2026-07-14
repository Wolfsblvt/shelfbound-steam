using System.Reflection;
using Microsoft.Extensions.Logging;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Enrichment;
using Shelfbound.Steam.Steam;
using Shelfbound.Steam.Web;
using Shelfbound.Storage;
using Shelfbound.Storage.Config;

namespace Shelfbound.Mcp;

/// <summary>
/// Loads and holds the library snapshot for the MCP server, and resolves the owner/profile id used by
/// the user-data store. Configuration comes from the saved config (<c>shelfbound setup</c>) and can be
/// overridden by environment variables an MCP client sets:
/// <list type="bullet">
///   <item><c>SHELFBOUND_SNAPSHOT</c> — path to an existing snapshot JSON to load (skips scanning).</item>
///   <item><c>SHELFBOUND_STEAM_PATH</c> — Steam install root (else auto-detected).</item>
///   <item><c>STEAM_WEB_API_KEY</c> — overrides the saved key; adds visible game/playtime observations.</item>
/// </list>
/// </summary>
public sealed class SnapshotContext(ISteamWebApiClient steamWebApiClient, IUserDataStore userDataStore, ILogger<SnapshotContext> logger)
{
    private SnapshotDocument? _snapshot;

    public SnapshotDocument Snapshot =>
        _snapshot ?? throw new InvalidOperationException("Snapshot has not been initialized.");

    /// <summary>Owner/profile id for the user-data store (the identity seam). See ProfileIdentity.</summary>
    public string OwnerId { get; private set; } = ProfileIdentity.LocalFallback;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ShelfboundConfig config = ShelfboundConfig.Load();

        string? snapshotFile = Environment.GetEnvironmentVariable("SHELFBOUND_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(snapshotFile) && File.Exists(snapshotFile))
        {
            SnapshotDocument loaded = SnapshotSerializer.Deserialize(await File.ReadAllTextAsync(snapshotFile, cancellationToken));
            _snapshot = loaded;
            OwnerId = ResolveOwner(config, loaded);
            LibraryScope operationalScope = LibraryScopeSemantics.GetOperationalScope(
                loaded.SchemaVersion,
                loaded.Stats.Scope);
            LibraryReconciler.RecordFirstSeen(userDataStore, OwnerId, loaded.Games.Select(g => g.AppId), operationalScope);
            logger.LogInformation("Loaded snapshot from {File} ({Games} games).", snapshotFile, loaded.Games.Count);
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
        OwnerId = ResolveOwner(config, snapshot);

        string? apiKey = Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY") ?? config.SteamApiKey;
        SteamAccount? account = snapshot.SteamAccounts.FirstOrDefault(a => a.MostRecent)
            ?? snapshot.SteamAccounts.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey) && account is not null)
        {
            try
            {
                OwnedGamesResult owned = await steamWebApiClient
                    .GetOwnedGamesAsync(account.SteamId64, apiKey, cancellationToken);
                if (owned.IsUsable)
                {
                    snapshot = SteamWebEnricher.Enrich(snapshot, scan.CategoriesByApp, owned.Games);
                    logger.LogInformation(
                        "Added {Observed} visible game observations from the Steam Web API response received at {ObservedAt}.",
                        owned.Games.Count,
                        owned.ObservedAt);
                }
                else
                {
                    logger.LogWarning(
                        "{Warning} Response received at {ObservedAt}.",
                        owned.Warning,
                        owned.ObservedAt);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Steam Web API enrichment failed; continuing with local data only.");
            }
        }

        _snapshot = snapshot;
        LibraryReconciler.RecordFirstSeen(userDataStore, OwnerId, snapshot.Games.Select(g => g.AppId), snapshot.Stats.Scope);
        logger.LogInformation("Snapshot ready: {Games} games, {Installed} installed, {Categories} categories (owner {Owner}).",
            snapshot.Games.Count, snapshot.Stats.InstalledGameCount, snapshot.Categories.Count, OwnerId);
    }

    private static string ResolveOwner(ShelfboundConfig config, SnapshotDocument snapshot)
    {
        SteamAccount? account = snapshot.SteamAccounts.FirstOrDefault(a => a.MostRecent)
            ?? snapshot.SteamAccounts.FirstOrDefault();
        return ProfileIdentity.Resolve(config, account?.SteamId64);
    }

    private static OsPlatform DetectOs()
    {
        if (OperatingSystem.IsWindows()) return OsPlatform.Windows;
        if (OperatingSystem.IsMacOS()) return OsPlatform.MacOs;
        if (OperatingSystem.IsLinux()) return OsPlatform.Linux;
        return OsPlatform.Unknown;
    }
}
