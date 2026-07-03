using Shelfbound.Core.Model;

namespace Shelfbound.Steam.Steam;

/// <summary>
/// Best-effort per-library storage classification for the desktop producers (CLI/tray/MCP). Maps a
/// library's drive to a <see cref="StorageKind"/> and reads its free/total capacity via
/// <see cref="DriveInfo"/>. Defensive: any failure yields null so the optional field is simply omitted
/// rather than failing the scan. It never emits a filesystem path, and never guesses — an ambiguous or
/// unreadable drive is <see cref="StorageKind.Unknown"/>.
/// </summary>
public static class StorageClassifier
{
    /// <summary>Describes the storage backing <paramref name="libraryPath"/>, or null if it can't be read.</summary>
    public static SnapshotStorage? Describe(string libraryPath)
    {
        try
        {
            string root = Path.GetPathRoot(libraryPath) is { Length: > 0 } pathRoot ? pathRoot : libraryPath;
            var drive = new DriveInfo(root);

            long? freeBytes = null;
            long? totalBytes = null;
            if (drive.IsReady)
            {
                freeBytes = drive.TotalFreeSpace;
                totalBytes = drive.TotalSize;
            }

            return new SnapshotStorage
            {
                Kind = Classify(drive.DriveType),
                FreeBytes = freeBytes,
                TotalBytes = totalBytes,
            };
        }
        catch
        {
            // DriveInfo can throw on an odd/unmapped root; storage is best-effort, so just omit it.
            return null;
        }
    }

    /// <summary>
    /// Maps a <see cref="DriveType"/> to a contract <see cref="StorageKind"/>. Removable maps to
    /// <see cref="StorageKind.External"/> — honest, because a desktop card reader can't be told apart
    /// from a USB stick here; the finer <see cref="StorageKind.SdCard"/> distinction is the Deck
    /// producer's job (from the mmc block-device name). Anything indeterminate is
    /// <see cref="StorageKind.Unknown"/>.
    /// </summary>
    public static StorageKind Classify(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => StorageKind.Internal,
        DriveType.Removable => StorageKind.External,
        DriveType.Network => StorageKind.Network,
        _ => StorageKind.Unknown,
    };
}
