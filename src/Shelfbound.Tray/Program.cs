using Avalonia;
using Velopack;

namespace Shelfbound.Tray;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles the installer's install/update/uninstall hooks and exits fast
        // for those. On a normal launch it returns immediately and the app starts as usual.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
