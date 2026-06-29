using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelfbound.Tray;

/// <summary>
/// Tray agent settings, persisted as JSON under the user's app-data folder. The API token is NOT kept
/// here — it lives in <see cref="TokenStore"/> (DPAPI/secret store) so the settings file holds no secret.
/// </summary>
public sealed class AppSettings
{
    // Default to localhost for now (nothing is deployed yet); production builds set the real URLs.
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string WebAppUrl { get; set; } = "http://localhost:5173";
    public string? DeviceName { get; set; }
    public bool AutoSync { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public bool StartMinimized { get; set; } = true;
    public bool StartOnLogin { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shelfbound", "tray.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable file: fall back to defaults rather than crashing the agent.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }
}
