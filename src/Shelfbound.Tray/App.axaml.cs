using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Shelfbound.Tray;

public partial class App : Application
{
    private SyncAgent? _agent;
    private UpdateService? _update;
    private MainWindow? _window;
    private NativeMenuItem? _updateItem;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The agent lives in the tray; closing the window hides it rather than quitting.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _agent = new SyncAgent();
            _update = new UpdateService();
            _window = new MainWindow(_agent, _update);
            _agent.Start();

            SetupTray(desktop);

            // Start hidden in the tray when configured — but always show on first run (not connected yet)
            // so the user sees the "Connect account" prompt instead of wondering where the app went.
            if (!_agent.Settings.StartMinimized || !_agent.IsConnected)
                ShowWindow();

            // Check for updates in the background — a no-op unless installed via the Velopack installer,
            // and only when the user hasn't turned automatic checks off. Manual "Check now" always works.
            _update.Changed += OnUpdateChanged;
            if (_agent.Settings.AutoUpdate)
                _ = _update.CheckAsync();
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
        var signOut = new NativeMenuItem("Sign out");
        signOut.Click += async (_, _) => await _agent!.SignOutAsync();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(sync);
        menu.Items.Add(connect);
        menu.Items.Add(signOut);

        // Only installed (Velopack) builds can self-update, so only surface the item there.
        if (_update?.IsSupported == true)
        {
            _updateItem = new NativeMenuItem("Check for updates…");
            _updateItem.Click += OnUpdateItemClick;
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(_updateItem);
        }

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        var trayIcon = new TrayIcon { Icon = icon, ToolTipText = "Shelfbound", Menu = menu, IsVisible = true };
        trayIcon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }

    private void OnUpdateItemClick(object? sender, EventArgs e)
    {
        if (_update is null)
            return;
        if (_update.State == UpdateState.ReadyToRestart)
            _update.ApplyAndRestart();
        else
            _ = _update.CheckAsync();
    }

    // Reflect update progress in the tray menu item (marshalled to the UI thread).
    private void OnUpdateChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_updateItem is null || _update is null)
            return;
        _updateItem.Header = _update.State switch
        {
            UpdateState.Checking => "Checking for updates…",
            UpdateState.Downloading => $"Downloading update {_update.TargetVersion}…",
            UpdateState.ReadyToRestart => $"Restart to update ({_update.TargetVersion})",
            _ => "Check for updates…",
        };
        _updateItem.IsEnabled = _update.State is UpdateState.UpToDate or UpdateState.Failed or UpdateState.ReadyToRestart;
    });

    private void ShowWindow()
    {
        if (_window is null)
            return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
