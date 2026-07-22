using System.Text.Json;
using System.Text.Json.Serialization;
using Shelfbound.Core.Model;

namespace Shelfbound.Tray;

/// <summary>
/// Tray agent settings, persisted as JSON under the user's app-data folder. The upload-only device token
/// is NOT kept here — it lives in <see cref="TokenStore"/> so the settings file holds no secret.
/// </summary>
public sealed class AppSettings
{
    private const long MaxSettingsFileBytes = 1024 * 1024;
    private const int MaxPrivateGameOverrides = 10_000;

    // Default to localhost for now (nothing is deployed yet); production builds set the real URLs.
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string WebAppUrl { get; set; } = "http://localhost:5173";
    public string? DeviceName { get; set; }
    [JsonConverter(typeof(SelectedDeviceTypeConverter))]
    public DeviceType? DeviceType { get; set; }
    public bool AutoSync { get; set; }
    public bool ExcludeSteamPrivateGames { get; set; }
    public List<int> PrivateGameUnskipAppIds { get; set; } = [];
    // Background upload stays disabled until the user previews and successfully sends this projection version.
    public string? HostedUploadConsentVersion { get; set; }
    public int IntervalMinutes { get; set; } = 60;
    public bool StartMinimized { get; set; } = true;
    public bool StartOnLogin { get; set; } = true;
    // Background update checks (installed builds only). Manual "Check now" works regardless.
    public bool AutoUpdate { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shelfbound", "tray.json");

    public static AppSettings Load() => Load(FilePath);

    internal static AppSettings Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                if (new FileInfo(filePath).Length > MaxSettingsFileBytes)
                    return new AppSettings();
                return Deserialize(File.ReadAllText(filePath));
            }
        }
        catch
        {
            // Corrupt/unreadable file: fall back to defaults rather than crashing the agent.
        }
        return new AppSettings();
    }

    public void Save() => Save(FilePath);

    internal void Save(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, Serialize(this));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { /* nothing to clean up */ }
        }
    }

    internal static AppSettings Deserialize(string json)
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        settings.PrivateGameUnskipAppIds = (settings.PrivateGameUnskipAppIds ?? [])
            .Where(appId => appId > 0)
            .Distinct()
            .Order()
            .Take(MaxPrivateGameOverrides)
            .ToList();
        return settings;
    }

    internal static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    private sealed class SelectedDeviceTypeConverter : JsonConverter<DeviceType?>
    {
        public override DeviceType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out DeviceType value) &&
                DeviceTypeSetup.IsComplete(value))
            {
                return value;
            }

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt32(out int number) &&
                Enum.IsDefined((DeviceType)number) &&
                DeviceTypeSetup.IsComplete((DeviceType)number))
            {
                return (DeviceType)number;
            }

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, DeviceType? value, JsonSerializerOptions options)
        {
            if (value is { } selected && DeviceTypeSetup.IsComplete(selected))
                writer.WriteStringValue(selected.ToString());
            else
                writer.WriteNullValue();
        }
    }
}
