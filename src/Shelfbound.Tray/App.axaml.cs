using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Shelfbound.Tray;

public partial class App : Application
{
    private SyncAgent? _agent;
    private MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The agent lives in the tray; closing the window hides it rather than quitting.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _agent = new SyncAgent();
            _window = new MainWindow(_agent);
            _agent.Start();

            SetupTray(desktop);

            if (!_agent.Settings.StartMinimized)
                ShowWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://Shelfbound.Tray/Assets/tray.png"));
        var icon = new WindowIcon(stream);

        var open = new NativeMenuItem("Open Shelfbound");
        open.Click += (_, _) => ShowWindow();
        var sync = new NativeMenuItem("Sync now");
        sync.Click += async (_, _) => await _agent!.SyncNowAsync();
        var connect = new NativeMenuItem("Connect account…");
        connect.Click += async (_, _) => await _agent!.ConnectAsync();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(sync);
        menu.Items.Add(connect);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        var trayIcon = new TrayIcon { Icon = icon, ToolTipText = "Shelfbound", Menu = menu, IsVisible = true };
        trayIcon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
