using Shelfbound.Core;
using Shelfbound.Core.Model;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Reads a local Steam installation and produces a versioned <see cref="SnapshotDocument"/>.
/// The scanner only emits games that are present in a local library (i.e. installed); owned games
/// that are not installed require the Steam Web API and are out of scope for the local v0 scan.
/// </summary>
public sealed class SteamScanner
{
    public ScanResult Scan(SteamScanRequest request)
    {
        var warnings = new List<string>();
        IReadOnlyList<SteamLibraryFolder> folders = ReadLibraries(request.SteamRootPath, warnings);

        var games = new List<SnapshotGame>();
        var libraries = new List<SnapshotLibrary>();

        foreach (var folder in folders)
        {
            int countInLibrary = 0;
            foreach (int appId in folder.AppIds)
            {
                string manifestPath = Path.Combine(folder.SteamAppsPath, $"appmanifest_{appId}.acf");
                if (!File.Exists(manifestPath))
                {
                    warnings.Add($"Missing manifest for app {appId} in library {folder.Index}.");
                    continue;
                }

                AppManifest manifest;
                try
                {
                    manifest = AppManifestParser.ParseFile(manifestPath);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to parse manifest for app {appId}: {ex.Message}");
                    continue;
                }

                games.Add(new SnapshotGame
                {
                    AppId = manifest.AppId,
                    Name = manifest.Name,
                    Installed = manifest.IsFullyInstalled,
                    LibraryIndex = folder.Index,
                    InstallDir = manifest.InstallDir,
                    SizeOnDiskBytes = manifest.SizeOnDisk,
                    LastUpdated = manifest.LastUpdated,
                    LastPlayed = manifest.LastPlayed,
                });
                countInLibrary++;
            }

            libraries.Add(new SnapshotLibrary
            {
                Index = folder.Index,
                Label = folder.Label,
                GameCount = countInLibrary,
            });
        }

        IReadOnlyList<SteamAccount> accounts = ReadAccounts(request.SteamRootPath, warnings);

        var snapshot = new SnapshotDocument
        {
            SchemaVersion = SnapshotSchema.Version,
            SnapshotId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow,
            Source = new SnapshotSource
            {
                Tool = SnapshotSchema.CliToolName,
                ToolVersion = request.ToolVersion,
                Platform = request.Device.Os,
            },
            Device = request.Device,
            SteamAccounts = accounts,
            Libraries = libraries,
            Games = games,
            Stats = new SnapshotStats
            {
                LibraryCount = libraries.Count,
                InstalledGameCount = games.Count(g => g.Installed),
                TotalSizeOnDiskBytes = games.Sum(g => g.SizeOnDiskBytes ?? 0),
            },
        };

        return new ScanResult { Snapshot = snapshot, Warnings = warnings };
    }

    private static IReadOnlyList<SteamLibraryFolder> ReadLibraries(string steamRoot, List<string> warnings)
    {
        string libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFoldersPath))
            return LibraryFoldersParser.ParseFile(libraryFoldersPath);

        warnings.Add($"libraryfolders.vdf not found at '{libraryFoldersPath}'; using the primary library only.");
        return [new SteamLibraryFolder { Index = 0, Path = steamRoot, Label = "library-0", AppIds = [] }];
    }

    private static IReadOnlyList<SteamAccount> ReadAccounts(string steamRoot, List<string> warnings)
    {
        string loginUsers = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(loginUsers))
        {
            warnings.Add("config/loginusers.vdf not found; no Steam accounts recorded.");
            return [];
        }

        try
        {
            return LoginUsersParser.ParseFile(loginUsers);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse loginusers.vdf: {ex.Message}");
            return [];
        }
    }
}
