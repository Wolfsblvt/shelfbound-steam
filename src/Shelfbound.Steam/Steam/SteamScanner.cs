using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Collections;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Reads a local Steam installation and produces a versioned <see cref="SnapshotDocument"/>.
/// Emits the installed games per library, the user's Steam accounts, and their local
/// collections/categories. Owned-but-not-installed games require the Steam Web API and are out of
/// scope for the local scan.
/// </summary>
public sealed class SteamScanner
{
    public ScanResult Scan(SteamScanRequest request)
    {
        var warnings = new List<string>();
        IReadOnlyList<SteamLibraryFolder> folders = ReadLibraries(request.SteamRootPath, warnings);
        IReadOnlyList<SteamAccount> accounts = ReadAccounts(request.SteamRootPath, warnings);
        IReadOnlyDictionary<int, IReadOnlyList<string>> categoriesByApp =
            ReadCategories(request.SteamRootPath, accounts, warnings);

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
                    Categories = categoriesByApp.TryGetValue(manifest.AppId, out var cats) ? cats : [],
                });
                countInLibrary++;
            }

            libraries.Add(new SnapshotLibrary
            {
                Index = folder.Index,
                Label = folder.Label,
                GameCount = countInLibrary,
                // Storage medium + free/total, classified from the (local-only) library path. Kind +
                // sizes only reach the contract; the path never does. Best-effort — null when unreadable.
                Storage = StorageClassifier.Describe(folder.Path),
            });
        }

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
            Categories = SummarizeCategories(categoriesByApp),
            Stats = new SnapshotStats
            {
                LibraryCount = libraries.Count,
                InstalledGameCount = games.Count(g => g.Installed),
                TotalSizeOnDiskBytes = games.Sum(g => g.SizeOnDiskBytes ?? 0),
            },
        };

        return new ScanResult { Snapshot = snapshot, Warnings = warnings, CategoriesByApp = categoriesByApp };
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

    /// <summary>
    /// Reads local categories for the most-recent account (falling back to any account that has them).
    /// Prefers the <b>modern</b> Steam collections (Chromium leveldb) and falls back to the legacy
    /// <c>sharedconfig.vdf</c> — the legacy file is stale for users who manage collections in the modern
    /// Steam UI. See docs/project/steam-collections.md.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<string>> ReadCategories(
        string steamRoot, IReadOnlyList<SteamAccount> accounts, List<string> warnings)
    {
        foreach (var account in accounts.OrderByDescending(a => a.MostRecent))
        {
            if (account.AccountId is not long accountId)
                continue;

            try
            {
                var modern = SteamCollectionsReader.TryRead(accountId);
                if (modern is { Count: > 0 })
                    return modern;
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to read modern collections for account {accountId}: {ex.Message}");
            }

            string path = Path.Combine(steamRoot, "userdata", accountId.ToString(), "7", "remote", "sharedconfig.vdf");
            if (!File.Exists(path))
                continue;

            try
            {
                return SharedConfigParser.ParseFile(path);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to parse sharedconfig.vdf for account {accountId}: {ex.Message}");
            }
        }

        if (accounts.Count > 0)
            warnings.Add("No categories found (modern collections or sharedconfig.vdf).");
        return new Dictionary<int, IReadOnlyList<string>>();
    }

    private static IReadOnlyList<SnapshotCategory> SummarizeCategories(
        IReadOnlyDictionary<int, IReadOnlyList<string>> categoriesByApp) =>
        categoriesByApp.Values
            .SelectMany(categories => categories)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Select(group => new SnapshotCategory { Name = group.Key, GameCount = group.Count() })
            .OrderByDescending(category => category.GameCount)
            .ThenBy(category => category.Name, StringComparer.Ordinal)
            .ToList();
}
