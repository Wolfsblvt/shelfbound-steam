using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelfbound.Storage.Config;

/// <summary>
/// Local app configuration (Steam Web API key, active profile), stored under the user's config dir —
/// never in the repo. The API key is the user's own read-only, rate-limited credential; it is stored
/// in the protected config directory in plaintext for now (OS keystore encryption is a planned
/// hardening — see docs/project/DECISIONS.md).
/// </summary>
public sealed record ShelfboundConfig
{
    public string? SteamApiKey { get; init; }
    public string? ActiveProfileId { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static ShelfboundConfig Load()
    {
        if (!File.Exists(ShelfboundPaths.ConfigFile))
            return new ShelfboundConfig();
        try
        {
            return JsonSerializer.Deserialize<ShelfboundConfig>(File.ReadAllText(ShelfboundPaths.ConfigFile), Options)
                ?? new ShelfboundConfig();
        }
        catch
        {
            return new ShelfboundConfig();
        }
    }

    public void Save()
    {
        Save(ShelfboundPaths.ConfigFile);
    }

    internal void Save(string path) =>
        PrivateFile.WriteAllTextAtomically(path, JsonSerializer.Serialize(this, Options));
}
