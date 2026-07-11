using System.Security.Cryptography;
using System.Text;

namespace Shelfbound.Tray;

/// <summary>
/// Stores the upload-only device token outside the plain settings file. On Windows it is DPAPI-encrypted
/// to the current user; on other OSes it is written to a 0600 file (libsecret/Keychain integration is a
/// TODO). Best-effort — a failure to read just means "not connected".
/// </summary>
public static class TokenStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shelfbound", "token.bin");

    public static void Save(string token) => Save(token, FilePath);

    internal static void Save(string token, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (OperatingSystem.IsWindows())
        {
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }
        else
        {
            File.WriteAllText(path, token);
            TryRestrictPermissions(path);
        }
    }

    public static string? Load() => Load(FilePath);

    internal static string? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    public static void Clear() => Clear(FilePath);

    internal static void Clear(string path)
    {
        try { File.Delete(path); } catch { /* nothing to clear */ }
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Permission tightening is best-effort.
        }
    }
}
