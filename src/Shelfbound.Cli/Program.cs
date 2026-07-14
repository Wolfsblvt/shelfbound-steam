using System.Reflection;
using Shelfbound.Client;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shelfbound.Query;
using Shelfbound.Steam.Steam;
using Shelfbound.Storage;
using Shelfbound.Storage.Config;

string version = ResolveVersion();

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

switch (args[0])
{
    case "-v" or "--version":
        Console.WriteLine($"shelfbound {version}");
        return 0;
    case "setup":
        return RunSetup(args);
    case "profile":
        return RunProfile(args);
    case "scan":
        return await RunScanAsync(args, version);
    case "upload":
        return await RunUploadAsync(args, version);
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        PrintUsage();
        return 2;
}

// --- commands ---

static async Task<int> RunScanAsync(string[] args, string version)
{
    string? steamPath = null, output = null, deviceName = null;
    DeviceType? deviceType = null;
    bool pretty = false, toStdout = false;

    for (int i = 1; i < args.Length; i++)
    {
        string a = args[i];
        switch (a)
        {
            case "--steam-path":
                if (!TryTakeValue(args, ref i, a, out steamPath)) return 2;
                break;
            case "-o" or "--output":
                if (!TryTakeValue(args, ref i, a, out output)) return 2;
                break;
            case "--device-name":
                if (!TryTakeValue(args, ref i, a, out deviceName)) return 2;
                break;
            case "--device-type":
                if (!TryParseDeviceType(args, ref i, out deviceType)) return 2;
                break;
            case "--pretty":
                pretty = true;
                break;
            case "--stdout":
                toStdout = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown option '{a}'.");
                PrintUsage();
                return 2;
        }
    }

    SnapshotBuildResult build;
    try
    {
        build = await SnapshotBuilder.BuildAsync(BuildOptions(steamPath, deviceName, deviceType, version));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    string json = SnapshotSerializer.Serialize(build.Snapshot, indented: pretty || toStdout);
    if (toStdout)
    {
        Console.Out.WriteLine(json);
    }
    else
    {
        string path = output ?? "shelfbound-snapshot.json";
        File.WriteAllText(path, json);
        PrintSummary(build.Snapshot, build.Device, path);
    }

    PrintWarnings(build.Warnings);
    return 0;
}

static SnapshotBuildOptions BuildOptions(string? steamPath, string? deviceName, DeviceType? deviceType,
    string version) => new()
    {
        SteamPath = steamPath,
        DeviceName = deviceName,
        DeviceType = deviceType,
        ToolVersion = version,
    };

static async Task<int> RunUploadAsync(string[] args, string version)
{
    string? server = Environment.GetEnvironmentVariable("SHELFBOUND_SERVER");
    string? token = Environment.GetEnvironmentVariable("SHELFBOUND_TOKEN");
    string? steamPath = null, deviceName = null;
    DeviceType? deviceType = null;
    bool watch = false, dryRun = false;
    int intervalSeconds = 0;

    for (int i = 1; i < args.Length; i++)
    {
        string a = args[i];
        switch (a)
        {
            case "--server":
                if (!TryTakeValue(args, ref i, a, out server)) return 2;
                break;
            case "--watch":
                watch = true;
                break;
            case "--dry-run":
                dryRun = true;
                break;
            case "--interval":
                if (!TryTakeValue(args, ref i, a, out string? iv)) return 2;
                if (!int.TryParse(iv, out intervalSeconds) || intervalSeconds < 0)
                {
                    Console.Error.WriteLine("--interval must be a non-negative number of seconds.");
                    return 2;
                }
                break;
            case "--steam-path":
                if (!TryTakeValue(args, ref i, a, out steamPath)) return 2;
                break;
            case "--device-name":
                if (!TryTakeValue(args, ref i, a, out deviceName)) return 2;
                break;
            case "--device-type":
                if (!TryParseDeviceType(args, ref i, out deviceType)) return 2;
                break;
            default:
                Console.Error.WriteLine($"Unknown option '{a}'.");
                PrintUsage();
                return 2;
        }
    }

    if (watch && dryRun)
    {
        Console.Error.WriteLine("--dry-run previews one exact upload and cannot be combined with --watch.");
        return 2;
    }

    if (!dryRun && string.IsNullOrWhiteSpace(server))
    {
        Console.Error.WriteLine("Set the Shelfbound server with --server <url> or the SHELFBOUND_SERVER environment variable.");
        return 2;
    }
    if (!dryRun && string.IsNullOrWhiteSpace(token))
    {
        Console.Error.WriteLine("Set an API token with the SHELFBOUND_TOKEN environment variable.");
        Console.Error.WriteLine("Create a token in the Shelfbound web app after signing in through Steam.");
        return 2;
    }

    SnapshotBuildOptions options = BuildOptions(steamPath, deviceName, deviceType, version);
    if (dryRun)
        return await PreviewUploadAsync(options);

    using var client = new ShelfboundClient(server!, token!);

    return watch
        ? await RunWatchAsync(client, options, intervalSeconds)
        : await UploadOnceAsync(client, options) ? 0 : 1;
}

// Writes the canonical compact body with no extra stdout text: these are the exact bytes UploadAsync sends.
static async Task<int> PreviewUploadAsync(SnapshotBuildOptions options)
{
    try
    {
        SnapshotBuildResult build = await SnapshotBuilder.BuildAsync(options);
        HostedUpload upload = HostedProjection.Prepare(build.Snapshot);
        Console.Out.Write(upload.Json);
        PrintWarnings(build.Warnings);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// Continuous sync. Automatic sync is a paid feature: refuse politely for non-entitled accounts. This
// client check is only a friendly fast-fail — the server also enforces a per-plan upload-frequency cap,
// which is the part that actually gates it (scheduling a one-shot upload externally just gets 429'd).
static async Task<int> RunWatchAsync(ShelfboundClient client, SnapshotBuildOptions options, int requestedIntervalSeconds)
{
    Entitlements? entitlements = await client.GetEntitlementsAsync();
    if (entitlements is null)
    {
        Console.Error.WriteLine("Could not reach the server to check your plan. Check --server and your token.");
        return 1;
    }
    if (!entitlements.AutoSync)
    {
        Console.Error.WriteLine($"Automatic sync is a Pro/Lifetime feature (your plan: {entitlements.Plan}).");
        Console.Error.WriteLine("A one-shot 'shelfbound upload' still works. Upgrade to enable --watch.");
        return 1;
    }

    int interval = Math.Max(requestedIntervalSeconds, entitlements.MinUploadIntervalSeconds);
    if (interval <= 0)
        interval = 3600;

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    Console.WriteLine($"Watching: syncing every {interval}s. Press Ctrl+C to stop.");

    while (!cts.IsCancellationRequested)
    {
        await UploadOnceAsync(client, options);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
        }
        catch (TaskCanceledException)
        {
            break;
        }
    }

    Console.WriteLine("Stopped.");
    return 0;
}

static async Task<bool> UploadOnceAsync(ShelfboundClient client, SnapshotBuildOptions options)
{
    SnapshotBuildResult build;
    try
    {
        build = await SnapshotBuilder.BuildAsync(options);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return false;
    }

    UploadResult result = await client.UploadAsync(build.Snapshot);
    switch (result.Status)
    {
        case UploadStatus.Success:
            Console.WriteLine($"Uploaded {result.GameCount} game(s).");
            if (!string.IsNullOrWhiteSpace(result.Warning))
                Console.Error.WriteLine($"warning: {result.Warning}");
            PrintScopeNotice(build.Snapshot);
            return true;
        case UploadStatus.Throttled:
            string retry = result.RetryAfterSeconds is { } s ? $" (try again in ~{s}s)" : "";
            Console.Error.WriteLine($"Upload rejected{retry}: {result.Message}");
            return false;
        case UploadStatus.Unauthorized:
            Console.Error.WriteLine($"Upload rejected: {result.Message}");
            return false;
        case UploadStatus.Forbidden:
            Console.Error.WriteLine($"Upload forbidden ({result.ErrorCode}): {result.Message}");
            return false;
        case UploadStatus.DeviceLimited:
            string cap = result.MaxDevices is { } max ? $" (maximum {max})" : "";
            Console.Error.WriteLine($"Upload rejected: {result.Message}{cap}");
            return false;
        case UploadStatus.InvalidSnapshot:
            Console.Error.WriteLine($"Upload rejected as invalid: {result.Message}");
            return false;
        case UploadStatus.PayloadTooLarge:
            Console.Error.WriteLine($"Upload rejected as too large: {result.Message}");
            return false;
        default:
            Console.Error.WriteLine($"Upload failed ({result.ErrorCode}): {result.Message}");
            return false;
    }
}

// --- helpers ---

static bool TryTakeValue(string[] args, ref int i, string option, out string? value)
{
    if (i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Option '{option}' requires a value.");
        value = null;
        return false;
    }
    value = args[++i];
    return true;
}

static bool TryParseDeviceType(string[] args, ref int i, out DeviceType? deviceType)
{
    deviceType = null;
    if (!TryTakeValue(args, ref i, "--device-type", out string? raw))
        return false;
    if (!Enum.TryParse(raw, ignoreCase: true, out DeviceType parsed))
    {
        Console.Error.WriteLine($"Invalid --device-type '{raw}'. Valid: {string.Join(", ", Enum.GetNames<DeviceType>())}.");
        return false;
    }
    deviceType = parsed;
    return true;
}

static void PrintWarnings(IReadOnlyList<string> warnings)
{
    foreach (string w in warnings.Take(10))
        Console.Error.WriteLine($"warning: {w}");
    if (warnings.Count > 10)
        Console.Error.WriteLine($"warning: ... and {warnings.Count - 10} more.");
}

static string ResolveVersion()
{
    string? raw = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (string.IsNullOrEmpty(raw))
        return "0.1.0";
    int plus = raw.IndexOf('+'); // strip source-control build metadata
    return plus >= 0 ? raw[..plus] : raw;
}

static void PrintSummary(SnapshotDocument s, SnapshotDevice device, string path)
{
    Console.WriteLine("Shelfbound snapshot written.");
    Console.WriteLine();
    Console.WriteLine($"  device      : {device.Name} ({device.Type}, {device.Os})");
    Console.WriteLine($"  device id   : {device.Id}");
    Console.WriteLine($"  steam users : {s.SteamAccounts.Count}");
    Console.WriteLine($"  libraries   : {s.Stats.LibraryCount}");
    foreach (var lib in s.Libraries)
        Console.WriteLine($"      - [{lib.Index}] {lib.Label}: {lib.GameCount} game(s)");
    Console.WriteLine($"  games       : {s.Games.Count} ({s.Stats.InstalledGameCount} fully installed)");
    Console.WriteLine($"  scope       : {DescribeScope(s.Stats.Scope)}");
    Console.WriteLine($"  on disk     : {FormatBytes(s.Stats.TotalSizeOnDiskBytes)}");
    Console.WriteLine($"  categories  : {s.Categories.Count}");
    foreach (var cat in s.Categories)
        Console.WriteLine($"      - {cat.Name}: {cat.GameCount}");
    Console.WriteLine($"  schema      : v{s.SchemaVersion}");
    Console.WriteLine($"  output      : {Path.GetFullPath(path)}");
    Console.WriteLine();
    PrintScopeNotice(s);
    Console.WriteLine("Privacy: this local snapshot is personal (Steam accounts + library data).");
    Console.WriteLine("No full install paths, credentials, or save data are included.");
    Console.WriteLine("See docs/project/privacy-and-data.md.");
}

static void PrintScopeNotice(SnapshotDocument s)
{
    switch (s.Stats.Scope)
    {
        case LibraryScope.FullLibrary:
            return;
        case LibraryScope.ObservedSubset:
            Console.WriteLine("Note: Steam supplied useful visible games and playtime, but does not guarantee a complete list.");
            Console.WriteLine("A missing game does not mean you do not own or have access to it.");
            Console.WriteLine();
            return;
        case LibraryScope.InstalledOnly:
            Console.WriteLine("Note: this snapshot includes only games observed installed on this device.");
            Console.WriteLine("A Steam Web API key may add visible games and playtime, but still cannot prove completeness:");
            Console.WriteLine("  shelfbound setup --steam-api-key-stdin   (then re-run)   https://steamcommunity.com/dev/apikey");
            Console.WriteLine();
            return;
        default:
            throw new ArgumentOutOfRangeException(nameof(s), s.Stats.Scope, "Unknown library scope.");
    }
}

static string DescribeScope(LibraryScope scope) => scope switch
{
    LibraryScope.InstalledOnly => "installed games observed on this device",
    LibraryScope.ObservedSubset => "observed subset (absence proves nothing)",
    LibraryScope.FullLibrary => "complete library (source contract)",
    _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown library scope."),
};

static int RunSetup(string[] args)
{
    var config = ShelfboundConfig.Load();
    string? newKey = null;
    bool keyFromEnvironment = false;
    bool keyFromStdin = false;
    bool show = false;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--steam-api-key-env":
                keyFromEnvironment = true;
                break;
            case "--steam-api-key-stdin":
                keyFromStdin = true;
                break;
            case "--show":
                show = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown setup option '{args[i]}'.");
                return 2;
        }
    }

    if (keyFromEnvironment && keyFromStdin)
    {
        Console.Error.WriteLine("Choose either --steam-api-key-env or --steam-api-key-stdin.");
        return 2;
    }

    if (keyFromEnvironment)
        newKey = Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");
    else if (keyFromStdin)
        newKey = Console.In.ReadLine();

    if ((keyFromEnvironment || keyFromStdin) && !TryNormalizeSecret(newKey, out newKey))
    {
        string source = keyFromEnvironment ? "STEAM_WEB_API_KEY" : "standard input";
        Console.Error.WriteLine($"No valid Steam Web API key was provided through {source}.");
        return 2;
    }

    if (newKey is not null)
    {
        config = config with { SteamApiKey = newKey };
        config.Save();
        Console.WriteLine($"Saved Steam Web API key to {ShelfboundPaths.ConfigFile}");
    }

    if (newKey is null || show)
    {
        Console.WriteLine("Shelfbound setup");
        Console.WriteLine($"  config file   : {ShelfboundPaths.ConfigFile}");
        Console.WriteLine($"  user data dir : {ShelfboundPaths.ProfilesDirectory}");
        Console.WriteLine($"  steam api key : {(string.IsNullOrEmpty(config.SteamApiKey) ? "(not set)" : Mask(config.SteamApiKey))}");
        Console.WriteLine();
        Console.WriteLine("Get a Steam Web API key at https://steamcommunity.com/dev/apikey, then:");
        Console.WriteLine("  shelfbound setup --steam-api-key-stdin   # reads one line from standard input");
        Console.WriteLine("  shelfbound setup --steam-api-key-env     # saves STEAM_WEB_API_KEY");
        Console.WriteLine("For owned-but-not-installed games, set your Steam profile 'Game details' to Public.");
    }
    return 0;
}

static string Mask(string value) => value.Length <= 4 ? "****" : new string('*', value.Length - 4) + value[^4..];

static bool TryNormalizeSecret(string? value, out string? normalized)
{
    normalized = value?.Trim();
    if (string.IsNullOrEmpty(normalized) || normalized.Length > 1024 || normalized.Any(char.IsControl))
    {
        normalized = null;
        return false;
    }
    return true;
}

static int RunProfile(string[] args)
{
    bool resetRecency = false;
    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--reset-recency":
                resetRecency = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown profile option '{args[i]}'.");
                return 2;
        }
    }

    var config = ShelfboundConfig.Load();
    string? steamRoot = SteamInstallLocator.Locate();
    if (steamRoot is null)
    {
        Console.Error.WriteLine("Could not find a Steam installation. Run 'shelfbound scan' first, or set SHELFBOUND_STEAM_PATH.");
        return 1;
    }

    ScanResult scan = new SteamScanner().Scan(new SteamScanRequest
    {
        SteamRootPath = steamRoot,
        Device = DeviceIdentity.Resolve(null, null),
        ToolVersion = ResolveVersion(),
    });

    SteamAccount? account = scan.Snapshot.SteamAccounts.FirstOrDefault(a => a.MostRecent)
        ?? scan.Snapshot.SteamAccounts.FirstOrDefault();
    string ownerId = ProfileIdentity.Resolve(config, account?.SteamId64);

    var store = new JsonUserDataStore(ShelfboundPaths.ProfilesDirectory);

    // Recovery path: forget the "recently added" baseline so the next scan re-establishes it from the
    // current library. Fixes a profile skewed by a scope change (owned games that a wider scan revealed
    // as if newly added). Only touches recency state — ratings, statuses, and memories are untouched.
    if (resetRecency)
    {
        store.Update(ownerId, profile => { UserDataActions.ResetRecencyBaseline(profile); return 0; });
        Console.WriteLine($"Reset the 'recently added' baseline for profile {ownerId}.");
        Console.WriteLine("The next scan (e.g. an MCP client connecting) re-establishes it: the current");
        Console.WriteLine("library becomes the baseline, and only games added later count as new.");
        return 0;
    }

    UserProfile profile = store.Load(ownerId);
    LibraryView view = LibraryViewBuilder.Build(scan.Snapshot, profile);
    ProfileSummary summary = ProfileQuery.Summarize(view);

    Console.WriteLine("What Shelfbound remembers");
    Console.WriteLine();
    Console.WriteLine($"  profile          : {ownerId}");
    Console.WriteLine($"  set up           : {(summary.IsSetUp ? "yes" : "no - rate a few games or tell Shelfbound your preferences")}");
    Console.WriteLine($"  rated games      : {summary.RatedGames}");
    Console.WriteLine($"  games w/ status  : {summary.GamesWithStatus}");
    Console.WriteLine($"  preferences      : {summary.GlobalPreferences}");
    Console.WriteLine($"  category meanings: {summary.CategoryMeanings} / {view.Categories.Count}");

    if (profile.CategoryDefinitions.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  category meanings:");
        foreach (var def in profile.CategoryDefinitions.Values.OrderBy(d => d.Name))
            Console.WriteLine($"      - {def.Name}: {def.Meaning}");
    }

    var preferences = profile.Memories.Where(m => m.Scope == MemoryScope.Global).ToList();
    if (preferences.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  preferences:");
        foreach (var memory in preferences)
            Console.WriteLine($"      - {memory.Text}");
    }

    var withData = view.Games.Where(g => g.UserData is not null).OrderBy(g => g.Name).ToList();
    if (withData.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  per-game:");
        foreach (var game in withData)
        {
            var bits = new List<string>();
            if (game.Status is { } s) bits.Add(s.ToString());
            if (game.Rating is { } r) bits.Add(r.ToString());
            if (game.CompletionPercent is { } c) bits.Add($"{c}%");
            Console.WriteLine($"      - {game.Name}: {string.Join(", ", bits)}");
        }
    }

    if (!summary.IsSetUp)
    {
        Console.WriteLine();
        Console.WriteLine("Tip: connect Shelfbound to an MCP client (Claude/ChatGPT) and it can ask about your");
        Console.WriteLine("favorites and save your taste — or record it yourself there.");
    }
    return 0;
}

static string FormatBytes(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double size = bytes;
    int unit = 0;
    while (size >= 1024 && unit < units.Length - 1)
    {
        size /= 1024;
        unit++;
    }
    return $"{size:0.##} {units[unit]}";
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        shelfbound - local Steam library scanner + uploader (Shelfbound)

        USAGE:
          shelfbound setup [--steam-api-key-stdin | --steam-api-key-env] [--show]
          shelfbound profile [--reset-recency]
          shelfbound scan [options]
          shelfbound upload [scan options] [--server <url>] [--dry-run] [--watch] [--interval <sec>]

        PROFILE OPTIONS:
          --reset-recency        Clear the "recently added" baseline; the next scan re-establishes it
                                 (use if a scope change made owned games look newly added)

        SCAN OPTIONS:
          --steam-path <dir>     Steam install root (else auto-detected / SHELFBOUND_STEAM_PATH)
          -o, --output <file>    Output file (default: shelfbound-snapshot.json)
          --stdout               Write snapshot JSON to stdout instead of a file
          --pretty               Indent the JSON output file
          --device-name <name>   Friendly device label (default: "Shelfbound device")
          --device-type <type>   desktop | laptop | steamDeck | server | unknown
          Set STEAM_WEB_API_KEY or save it with `shelfbound setup`; secrets are not accepted in argv

        UPLOAD OPTIONS (also accepts the scan options above):
          --server <url>         Shelfbound server (or set SHELFBOUND_SERVER)
          Set the API token from the web app with SHELFBOUND_TOKEN; secrets are not accepted in argv
          --dry-run              Print the exact privacy-minimized upload body; sends nothing
          --watch                Keep syncing on an interval (requires a Pro/Lifetime plan)
          --interval <seconds>   Watch interval (clamped to your plan's minimum)

        GLOBAL:
          -v, --version          Print version
          -h, --help             Show this help

        Produces a versioned Shelfbound snapshot of locally installed Steam games, and optionally
        uploads it to a Shelfbound server. See docs/project/snapshot-schema.md.
        """);
}
