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
        // TODO: Linux (~/.config/autostart/shelfbound.desktop) and macOS (LaunchAgent plist).
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
