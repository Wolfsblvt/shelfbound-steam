using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Shelfbound.Tray;

public partial class MainWindow : Window
{
    private readonly SyncAgent _agent;
    private readonly UpdateService? _update;
    private bool _loading;

    public MainWindow(SyncAgent agent, UpdateService? update = null)
    {
        _agent = agent;
        _update = update;
        InitializeComponent();
        LoadSettings();
        WireEvents();
        _agent.Changed += OnAgentChanged;
        if (_update is not null)
            _update.Changed += OnUpdateChanged;
        Refresh();
        RefreshUpdate();
    }

    // For the Avalonia runtime loader / designer only; the app always uses the agent ctor above.
    public MainWindow() : this(new SyncAgent()) { }

    private void LoadSettings()
    {
        _loading = true;
        AutoSyncCheck.IsChecked = _agent.Settings.AutoSync;
        IntervalInput.Value = _agent.Settings.IntervalMinutes;
        StartLoginCheck.IsChecked = _agent.Settings.StartOnLogin;
        StartMinimizedCheck.IsChecked = _agent.Settings.StartMinimized;
        AutoUpdateCheck.IsChecked = _agent.Settings.AutoUpdate;
        _loading = false;
        UpdateSyncEnabledState();
    }

    private void WireEvents()
    {
        VersionText.Text = $"Shelfbound Tray v{AppInfo.Version}";
        SyncButton.Click += async (_, _) => await _agent.SyncNowAsync();
        ConnectButton.Click += async (_, _) => await _agent.ConnectAsync();
        SignOutButton.Click += async (_, _) => await _agent.SignOutAsync();
        ManageDevicesButton.Click += (_, _) => Browser.Open(_agent.Settings.WebAppUrl);
        UpdateRestartButton.Click += (_, _) => _update?.ApplyAndRestart();
        CheckUpdateButton.Click += (_, _) => { _ = _update?.CheckAsync(); };
        ReleaseNotesLink.PointerPressed += (_, _) => Browser.Open(AppInfo.ReleasesUrl);
        UpdateNotesLink.PointerPressed += (_, _) => Browser.Open(_update?.TargetReleaseUrl ?? AppInfo.ReleasesUrl);
        BugReportLink.PointerPressed += (_, _) => Browser.Open(AppInfo.IssuesUrl);
        AutoSyncCheck.IsCheckedChanged += (_, _) => Apply();
        StartLoginCheck.IsCheckedChanged += (_, _) => Apply();
        StartMinimizedCheck.IsCheckedChanged += (_, _) => Apply();
        AutoUpdateCheck.IsCheckedChanged += (_, _) => Apply();
        IntervalInput.ValueChanged += (_, _) => Apply();
        // Drag the window by the custom title bar (we draw our own chrome).
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        ShowSpecs();
    }

    private void ShowSpecs()
    {
        var s = _agent.Specs;
        SpecOs.Text = s.OsDescription ?? "—";
        SpecCpu.Text = s.Cpu ?? "—";
        SpecCores.Text = s.LogicalCores?.ToString() ?? "—";
        SpecRam.Text = s.TotalMemoryBytes is { } bytes ? $"{bytes / 1024d / 1024d / 1024d:0.#} GB" : "—";
        SpecGpu.Text = s.Gpu ?? "—";
    }

    private void Apply()
    {
        if (_loading)
            return;
        _agent.UpdateSettings(s =>
        {
            s.AutoSync = AutoSyncCheck.IsChecked ?? false;
            s.IntervalMinutes = (int)(IntervalInput.Value ?? 60m);
            s.StartOnLogin = StartLoginCheck.IsChecked ?? false;
            s.StartMinimized = StartMinimizedCheck.IsChecked ?? false;
            s.AutoUpdate = AutoUpdateCheck.IsChecked ?? true;
        });
        UpdateSyncEnabledState();
    }

    // The interval only applies to background auto-sync — grey it out when that's off.
    private void UpdateSyncEnabledState() => IntervalRow.IsEnabled = AutoSyncCheck.IsChecked ?? false;

    private void OnAgentChanged() => Dispatcher.UIThread.Post(Refresh);
    private void OnUpdateChanged() => Dispatcher.UIThread.Post(RefreshUpdate);

    private void Refresh()
    {
        StatusText.Text = _agent.StatusLine;
        DeviceText.Text = _agent.IsConnected
            ? $"Device: {_agent.Settings.DeviceName ?? Environment.MachineName}"
            : "Sign in to connect this device.";
        HistoryList.ItemsSource = _agent.History;
        RefreshAccount();
    }

    private void RefreshAccount()
    {
        bool connected = _agent.IsConnected;
        ConnectButton.IsVisible = !connected;
        SignOutButton.IsVisible = connected;

        if (!connected)
        {
            AccountName.Text = "Not signed in";
            AccountPlan.Text = "Connect this device to your Shelfbound account.";
            DevicesSection.IsVisible = false;
            return;
        }

        string deviceName = _agent.Settings.DeviceName ?? Environment.MachineName;
        AccountName.Text = deviceName;
        AccountPlan.Text = "Connected (upload-only)";
        ThisDeviceText.Text = $"This device: {deviceName}";
        // TODO(dashboard): link directly to the device-management page once the dashboard UI ships.
        DevicesSection.IsVisible = true;
    }

    private void RefreshUpdate()
    {
        // Banner across the top — only while an update is downloading or ready to install.
        UpdateBanner.IsVisible = _update?.State is UpdateState.Downloading or UpdateState.ReadyToRestart;
        if (_update?.State == UpdateState.Downloading)
        {
            UpdateText.Text = $"Downloading update {_update.TargetVersion}…";
            UpdateRestartButton.IsVisible = false;
            UpdateNotesLink.IsVisible = false;
        }
        else if (_update?.State == UpdateState.ReadyToRestart)
        {
            UpdateText.Text = $"Update {_update.TargetVersion} is ready.";
            UpdateRestartButton.IsVisible = true;
            UpdateNotesLink.IsVisible = true;
        }

        // UPDATES card. Only installed (Velopack) builds can self-update; dev/source runs say so.
        if (_update is null || !_update.IsSupported)
        {
            UpdateStatusText.Text = "Automatic updates apply to installed builds only.";
            UpdateActions.IsVisible = false;
            return;
        }

        UpdateActions.IsVisible = true;
        UpdateStatusText.Text = _update.State switch
        {
            UpdateState.Checking => "Checking for updates…",
            UpdateState.Downloading => $"Downloading update {_update.TargetVersion}…",
            UpdateState.ReadyToRestart => $"Update {_update.TargetVersion} downloaded — restart to apply.",
            UpdateState.Failed => "Last update check failed.",
            _ => "You're on the latest version.",
        };
        CheckUpdateButton.IsEnabled = _update.State is not (UpdateState.Checking or UpdateState.Downloading);
        LastCheckedText.Text = _update.LastChecked is { } t ? $"Checked {Ago(t)}" : "";
    }

    private static string Ago(DateTimeOffset when)
    {
        TimeSpan span = DateTimeOffset.Now - when;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        return when.ToString("yyyy-MM-dd");
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Hide to the tray instead of quitting; the agent keeps running in the background.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
