using System.Runtime.InteropServices;
using Shelfbound.Core.Model;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Resolves the local device identity for a snapshot. The device id is a random GUID persisted
/// under the user's config directory so it stays stable across runs without being derived from
/// hardware or account data. See docs/project/privacy-and-data.md.
/// </summary>
public static class DeviceIdentity
{
    public static SnapshotDevice Resolve(string? nameOverride, DeviceType? typeOverride) => new()
    {
        Id = GetOrCreateDeviceId(),
        Name = NormalizeName(nameOverride),
        Type = typeOverride ?? DetectType(),
        Os = DetectOs(),
        Specs = HardwareInfo.Collect(),
    };

    /// <summary>
    /// Resolves and validates the snapshot device name. The same normalized value is used by native
    /// connect-code binding and snapshot uploads, so surrounding whitespace can never create two
    /// different device identities.
    /// </summary>
    public static string NormalizeName(string? nameOverride)
    {
        string candidate = string.IsNullOrWhiteSpace(nameOverride)
            ? Environment.MachineName
            : nameOverride;
        string name = candidate.Trim();

        if (name.Length == 0)
            throw new ArgumentException("The device name cannot be empty.", nameof(nameOverride));
        if (name.Length > 200)
            throw new ArgumentException("The device name cannot exceed 200 characters.", nameof(nameOverride));
        if (name.Any(char.IsControl))
            throw new ArgumentException("The device name cannot contain control characters.", nameof(nameOverride));

        return name;
    }

    private static OsPlatform DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OsPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OsPlatform.MacOs;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OsPlatform.Linux;
        return OsPlatform.Unknown;
    }

    private static DeviceType DetectType()
    {
        // Steam Deck runs SteamOS (Linux) under a 'deck' user. Best-effort detection only;
        // users can always override with --device-type.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            (Directory.Exists("/home/deck") || Environment.UserName == "deck"))
        {
            return DeviceType.SteamDeck;
        }
        return DeviceType.Unknown;
    }

    private static string GetOrCreateDeviceId()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Shelfbound");
        string file = Path.Combine(dir, "device-id");

        try
        {
            if (File.Exists(file))
            {
                string existing = File.ReadAllText(file).Trim();
                if (Guid.TryParse(existing, out _))
                    return existing;
            }

            Directory.CreateDirectory(dir);
            string id = Guid.NewGuid().ToString("D");
            File.WriteAllText(file, id);
            return id;
        }
        catch
        {
            // If we cannot persist, fall back to an ephemeral id rather than failing the scan.
            return Guid.NewGuid().ToString("D");
        }
    }
}
