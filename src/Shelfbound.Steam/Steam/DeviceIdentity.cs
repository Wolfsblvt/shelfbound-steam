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
        Name = nameOverride ?? Environment.MachineName,
        Type = typeOverride ?? DetectType(),
        Os = DetectOs(),
    };

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
