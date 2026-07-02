using System.Diagnostics;

namespace Shelfbound.Tray;

/// <summary>Opens a URL in the user's default browser. Best-effort — never throws.</summary>
public static class Browser
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If the launcher fails the user can navigate manually; not worth crashing over.
        }
    }
}
