using System.Text;

namespace Shelfbound.Storage;

/// <summary>Atomic writes for local secret or personal-data files.</summary>
internal static class PrivateFile
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void WriteAllTextAtomically(string path, string content)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A private file path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = OwnerReadWrite;

            using (var stream = new FileStream(temporaryPath, options))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                writer.Write(content);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, OwnerReadWrite);

            File.Move(temporaryPath, path, overwrite: true);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, OwnerReadWrite);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch { /* best-effort cleanup; the destination has already been written or the caller gets the error */ }
        }
    }
}
