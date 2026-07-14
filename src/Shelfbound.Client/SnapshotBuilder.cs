using Shelfbound.Core.Model;
using Shelfbound.Steam.Enrichment;
using Shelfbound.Steam.Steam;
using Shelfbound.Steam.Web;
using Shelfbound.Storage.Config;

namespace Shelfbound.Client;

/// <summary>Options for producing a library snapshot. Any unset value falls back to auto-detection.</summary>
public sealed record SnapshotBuildOptions
{
    public string? SteamPath { get; init; }
    public string? DeviceName { get; init; }
    public DeviceType? DeviceType { get; init; }
    public string? SteamApiKey { get; init; }
    public required string ToolVersion { get; init; }
}

/// <summary>A produced snapshot plus the resolved device and any non-fatal warnings.</summary>
public sealed record SnapshotBuildResult(SnapshotDocument Snapshot, SnapshotDevice Device, IReadOnlyList<string> Warnings);

/// <summary>
/// Scans the local Steam library (and optionally enriches it via the Steam Web API) into a snapshot.
/// The one place that logic lives, reused by the CLI and the tray agent. Returns warnings rather than
/// writing to the console, and throws if Steam can't be located.
/// </summary>
public static class SnapshotBuilder
{
    public static async Task<SnapshotBuildResult> BuildAsync(SnapshotBuildOptions options, CancellationToken ct = default)
    {
        string? steamRoot = SteamInstallLocator.Locate(options.SteamPath);
        if (steamRoot is null)
        {
            throw new InvalidOperationException(options.SteamPath is null
                ? "Could not find a Steam installation. Set the Steam path or SHELFBOUND_STEAM_PATH."
                : $"'{options.SteamPath}' does not look like a Steam installation (no steamapps folder).");
        }

        SnapshotDevice device = DeviceIdentity.Resolve(options.DeviceName, options.DeviceType);
        ScanResult result = new SteamScanner().Scan(new SteamScanRequest
        {
            SteamRootPath = steamRoot,
            Device = device,
            ToolVersion = options.ToolVersion,
        });

        var warnings = new List<string>();

        // Optional Steam Web API enrichment: positive visible game/playtime observations.
        string? apiKey = options.SteamApiKey
            ?? Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY")
            ?? ShelfboundConfig.Load().SteamApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            SteamAccount? account = result.Snapshot.SteamAccounts.FirstOrDefault(a => a.MostRecent)
                ?? result.Snapshot.SteamAccounts.FirstOrDefault();
            if (account is null)
            {
                warnings.Add("Steam Web API key set but no Steam account was found to query.");
            }
            else
            {
                try
                {
                    using var http = new HttpClient();
                    OwnedGamesResult owned = await new SteamWebApiClient(http)
                        .GetOwnedGamesAsync(account.SteamId64, apiKey, ct);
                    if (owned.IsUsable)
                    {
                        result = result with
                        {
                            Snapshot = SteamWebEnricher.Enrich(
                                result.Snapshot,
                                result.CategoriesByApp,
                                owned.Games),
                        };
                    }
                    else
                    {
                        warnings.Add(owned.Warning!);
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Steam Web API enrichment failed: {ex.Message}");
                }
            }
        }

        warnings.AddRange(result.Warnings);
        return new SnapshotBuildResult(result.Snapshot, device, warnings);
    }
}
