using System.Reflection;
using Shelfbound.Cli;
using Shelfbound.Core;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

string version = ResolveVersion();

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

if (args[0] is "-v" or "--version")
{
    Console.WriteLine($"shelfbound {version}");
    return 0;
}

if (args[0] != "scan")
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    PrintUsage();
    return 2;
}

// --- parse `scan` options ---
string? steamPath = null;
string? output = null;
string? deviceName = null;
DeviceType? deviceType = null;
bool pretty = false;
bool toStdout = false;

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
            if (!TryTakeValue(args, ref i, a, out string? raw)) return 2;
            if (!Enum.TryParse(raw, ignoreCase: true, out DeviceType parsed))
            {
                Console.Error.WriteLine($"Invalid --device-type '{raw}'. Valid: {string.Join(", ", Enum.GetNames<DeviceType>())}.");
                return 2;
            }
            deviceType = parsed;
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

// --- locate Steam ---
string? steamRoot = SteamInstallLocator.Locate(steamPath);
if (steamRoot is null)
{
    Console.Error.WriteLine(steamPath is null
        ? "Could not find a Steam installation. Pass --steam-path <dir> or set SHELFBOUND_STEAM_PATH."
        : $"'{steamPath}' does not look like a Steam installation (no steamapps folder).");
    return 1;
}

// --- scan ---
SnapshotDevice device = DeviceIdentity.Resolve(deviceName, deviceType);
ScanResult result = new SteamScanner().Scan(new SteamScanRequest
{
    SteamRootPath = steamRoot,
    Device = device,
    ToolVersion = version,
});

string json = SnapshotSerializer.Serialize(result.Snapshot, indented: pretty || toStdout);

if (toStdout)
{
    Console.Out.WriteLine(json);
}
else
{
    string path = output ?? "shelfbound-snapshot.json";
    File.WriteAllText(path, json);
    PrintSummary(result, device, path);
}

foreach (string w in result.Warnings.Take(10))
    Console.Error.WriteLine($"warning: {w}");
if (result.Warnings.Count > 10)
    Console.Error.WriteLine($"warning: ... and {result.Warnings.Count - 10} more.");

return 0;

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

static string ResolveVersion()
{
    string? raw = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (string.IsNullOrEmpty(raw))
        return "0.1.0";
    int plus = raw.IndexOf('+'); // strip source-control build metadata
    return plus >= 0 ? raw[..plus] : raw;
}

static void PrintSummary(ScanResult result, SnapshotDevice device, string path)
{
    var s = result.Snapshot;
    Console.WriteLine("Shelfbound snapshot written.");
    Console.WriteLine();
    Console.WriteLine($"  device      : {device.Name} ({device.Type}, {device.Os})");
    Console.WriteLine($"  device id   : {device.Id}");
    Console.WriteLine($"  steam users : {s.SteamAccounts.Count}");
    Console.WriteLine($"  libraries   : {s.Stats.LibraryCount}");
    foreach (var lib in s.Libraries)
        Console.WriteLine($"      - [{lib.Index}] {lib.Label}: {lib.GameCount} game(s)");
    Console.WriteLine($"  games       : {s.Games.Count} ({s.Stats.InstalledGameCount} fully installed)");
    Console.WriteLine($"  on disk     : {FormatBytes(s.Stats.TotalSizeOnDiskBytes)}");
    Console.WriteLine($"  schema      : v{s.SchemaVersion}");
    Console.WriteLine($"  output      : {Path.GetFullPath(path)}");
    Console.WriteLine();
    Console.WriteLine("Privacy: no install paths, credentials, or save data are included.");
    Console.WriteLine("See docs/project/privacy-and-data.md.");
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
        shelfbound - local Steam library scanner (Shelfbound)

        USAGE:
          shelfbound scan [options]

        OPTIONS:
          --steam-path <dir>     Steam install root (else auto-detected / SHELFBOUND_STEAM_PATH)
          -o, --output <file>    Output file (default: shelfbound-snapshot.json)
          --stdout               Write snapshot JSON to stdout instead of a file
          --pretty               Indent the JSON output file
          --device-name <name>   Override device name (default: machine name)
          --device-type <type>   desktop | laptop | steamDeck | server | unknown
          -v, --version          Print version
          -h, --help             Show this help

        Produces a versioned Shelfbound snapshot of locally installed Steam games.
        See docs/project/snapshot-schema.md.
        """);
}
