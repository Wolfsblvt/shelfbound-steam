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
    string? steamPath = null, output = null, deviceName = null, steamApiKey = null;
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
            case "--steam-api-key":
                if (!TryTakeValue(args, ref i, a, out steamApiKey)) return 2;
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
        build = await SnapshotBuilder.BuildAsync(BuildOptions(steamPath, deviceName, deviceType, steamApiKey, version));
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
    string? steamApiKey, string version) => new()
{
    SteamPath = steamPath,
    DeviceName = deviceName,
    DeviceType = deviceType,
    SteamApiKey = steamApiKey,
    ToolVersion = version,
};

static async Task<int> RunUploadAsync(string[] args, string version)
{
    string? server = Environment.GetEnvironmentVariable("SHELFBOUND_SERVER");
    string? token = Environment.GetEnvironmentVariable("SHELFBOUND_TOKEN");
    string? steamPath = null, deviceName = null, steamApiKey = null;
    DeviceType? deviceType = null;
    bool watch = false;
    int intervalSeconds = 0;

    for (int i = 1; i < args.Length; i++)
    {
        string a = args[i];
        switch (a)
        {
            case "--server":
                if (!TryTakeValue(args, ref i, a, out server)) return 2;
                break;
            case "--token":
                if (!TryTakeValue(args, ref i, a, out token)) return 2;
                break;
            case "--watch":
                watch = true;
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
            case "--steam-api-key":
                if (!TryTakeValue(args, ref i, a, out steamApiKey)) return 2;
                break;
            default:
                Console.Error.WriteLine($"Unknown option '{a}'.");
                PrintUsage();
                return 2;
        }
    }

    if (string.IsNullOrWhiteSpace(server))
    {
        Console.Error.WriteLine("Set the Shelfbound server with --server <url> or the SHELFBOUND_SERVER environment variable.");
        return 2;
    }
    if (string.IsNullOrWhiteSpace(token))
    {
        Console.Error.WriteLine("Set an API token with --token <token> or the SHELFBOUND_TOKEN environment variable.");
        Console.Error.WriteLine("Create a token in the Shelfbound web app after signing in through Steam.");
        return 2;
    }

    SnapshotBuildOptions options = BuildOptions(steamPath, deviceName, deviceType, steamApiKey, version);
    using var client = new ShelfboundClient(server, token);

    return watch
        ? await RunWatchAsync(client, options, intervalSeconds)
        : await UploadOnceAsync(client, options) ? 0 : 1;
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
            PrintScopeNotice(build.Snapshot);
            return true;
        case UploadStatus.Throttled:
            string retry = result.RetryAfterSeconds is { } s ? $" (try again in ~{s}s)" : "";
            Console.Error.WriteLine($"Upload rejected: too soon for your plan{retry}. Automatic/frequent sync is a Pro/Lifetime feature.");
            return false;
        case UploadStatus.Unauthorized:
            Console.Error.WriteLine("Upload rejected: invalid or missing API token. Create one in the Shelfbound web app.");
            return false;
        case UploadStatus.DeviceNameMismatch:
            Console.Error.WriteLine("Upload rejected: the snapshot device name does not match this device token. Reconnect the device.");
            return false;
        default:
            Console.Error.WriteLine($"Upload failed: {result.Message}");
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
    bool fullLibrary = s.Stats.Scope == LibraryScope.FullLibrary;
    Console.WriteLine($"  games       : {s.Games.Count} ({s.Stats.InstalledGameCount} fully installed)");
    Console.WriteLine($"  scope       : {(fullLibrary ? "full owned library" : "installed games only")}");
    Console.WriteLine($"  on disk     : {FormatBytes(s.Stats.TotalSizeOnDiskBytes)}");
    Console.WriteLine($"  categories  : {s.Categories.Count}");
    foreach (var cat in s.Categories)
        Console.WriteLine($"      - {cat.Name}: {cat.GameCount}");
    Console.WriteLine($"  schema      : v{s.SchemaVersion}");
    Console.WriteLine($"  output      : {Path.GetFullPath(path)}");
    Console.WriteLine();
    PrintScopeNotice(s);
    Console.WriteLine("Privacy: no install paths, credentials, or save data are included.");
    Console.WriteLine("See docs/project/privacy-and-data.md.");
}

// When a snapshot is installed-only, say so loudly: it's the difference between "I don't own Portal"
// and "Portal just isn't installed". The fix is a Steam Web API key.
static void PrintScopeNotice(SnapshotDocument s)
{
    if (s.Stats.Scope == LibraryScope.FullLibrary)
        return;
    Console.WriteLine("Note: this snapshot includes only installed games. Owned-but-not-installed games");
    Console.WriteLine("are missing. Add them with a free Steam Web API key:");
    Console.WriteLine("  shelfbound setup --steam-api-key <key>   (then re-run)   https://steamcommunity.com/dev/apikey");
    Console.WriteLine();
}

static int RunSetup(string[] args)
{
    var config = ShelfboundConfig.Load();
    string? newKey = null;
    bool show = false;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--steam-api-key":
                if (!TryTakeValue(args, ref i, "--steam-api-key", out newKey)) return 2;
                break;
            case "--show":
                show = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown setup option '{args[i]}'.");
                return 2;
        }
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
        Console.WriteLine("  shelfbound setup --steam-api-key <key>");
        Console.WriteLine("For owned-but-not-installed games, set your Steam profile 'Game details' to Public.");
    }
    return 0;
}

static string Mask(string value) => value.Length <= 4 ? "****" : new string('*', value.Length - 4) + value[^4..];

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
          shelfbound setup [--steam-api-key <key>] [--show]
          shelfbound profile [--reset-recency]
          shelfbound scan [options]
          shelfbound upload [scan options] [--server <url>] [--token <tok>] [--watch] [--interval <sec>]

        PROFILE OPTIONS:
          --reset-recency        Clear the "recently added" baseline; the next scan re-establishes it
                                 (use if a scope change made owned games look newly added)

        SCAN OPTIONS:
          --steam-path <dir>     Steam install root (else auto-detected / SHELFBOUND_STEAM_PATH)
          -o, --output <file>    Output file (default: shelfbound-snapshot.json)
          --stdout               Write snapshot JSON to stdout instead of a file
          --pretty               Indent the JSON output file
          --device-name <name>   Override device name (default: machine name)
          --device-type <type>   desktop | laptop | steamDeck | server | unknown
          --steam-api-key <key>  Add owned (not installed) games + playtime via the Steam Web API
                                 (or set STEAM_WEB_API_KEY)

        UPLOAD OPTIONS (also accepts the scan options above):
          --server <url>         Shelfbound server (or set SHELFBOUND_SERVER)
          --token <token>        API token from the web app (or set SHELFBOUND_TOKEN)
          --watch                Keep syncing on an interval (requires a Pro/Lifetime plan)
          --interval <seconds>   Watch interval (clamped to your plan's minimum)

        GLOBAL:
          -v, --version          Print version
          -h, --help             Show this help

        Produces a versioned Shelfbound snapshot of locally installed Steam games, and optionally
        uploads it to a Shelfbound server. See docs/project/snapshot-schema.md.
        """);
}
