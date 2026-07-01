using System.Runtime.Versioning;

namespace Shelfbound.Tray;

/// <summary>
/// Registers (or removes) the agent from OS login start-up. Windows is implemented via the per-user Run
/// key; Linux (~/.config/autostart) and macOS (LaunchAgent) are TODO. Best-effort and non-fatal.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Shelfbound";

    public static void Apply(bool enabled)
    {
        if (OperatingSystem.IsWindows())
            ApplyWindows(enabled);
        else if (OperatingSystem.IsLinux())
            ApplyLinux(enabled);
        else if (OperatingSystem.IsMacOS())
            ApplyMacOs(enabled);
    }

    private static void ApplyLinux(bool enabled)
    {
        try
        {
            // SpecialFolder.ApplicationData is ~/.config on Linux.
            string file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", "shelfbound.desktop");
            // When packaged as an AppImage, ProcessPath points at a transient mount (…/.mount_XXXX/…) that
            // is gone after a reboot; $APPIMAGE holds the stable path to the AppImage itself. Prefer it.
            string? launcher = Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath;
            if (enabled && launcher is { } exe)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file,
                    $"[Desktop Entry]\nType=Application\nName=Shelfbound\nExec=\"{exe}\"\nX-GNOME-Autostart-enabled=true\n");
            }
            else if (!enabled && File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch { /* non-fatal */ }
    }

    private static void ApplyMacOs(bool enabled)
    {
        try
        {
            string file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents", "com.shelfbound.tray.plist");
            if (enabled && Environment.ProcessPath is { } exe)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<plist version=\"1.0\"><dict>" +
                    "<key>Label</key><string>com.shelfbound.tray</string>" +
                    $"<key>ProgramArguments</key><array><string>{exe}</string></array>" +
                    "<key>RunAtLoad</key><true/></dict></plist>\n");
            }
            else if (!enabled && File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch { /* non-fatal */ }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(bool enabled)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
                return;

            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else if (!enabled)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Registry access can fail (policy, permissions); auto-start is a convenience, not critical.
        }
    }
}
