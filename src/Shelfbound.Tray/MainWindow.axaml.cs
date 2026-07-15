using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Tray;

public partial class MainWindow : Window
{
    private readonly SyncAgent _agent;
    private readonly UpdateService? _update;
    private bool _loading;
    private bool _syncInProgress;

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
        DeviceTypeInput.ItemsSource = DeviceTypeSetup.Choices;
        DeviceType? initialDeviceType = DeviceTypeSetup.GetInitialSelection(
            _agent.Settings.DeviceType,
            _agent.SuggestedDeviceType);
        DeviceTypeInput.SelectedItem = DeviceTypeSetup.Choices.FirstOrDefault(choice =>
            choice.Type == initialDeviceType);
        _loading = false;
        UpdateSyncEnabledState();
        UpdateDeviceTypeSaveState();
    }

    private void WireEvents()
    {
        VersionText.Text = $"Shelfbound Tray v{AppInfo.Version}";
        SyncButton.Click += async (_, _) => await PreviewAndSyncAsync();
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
        DeviceTypeInput.SelectionChanged += (_, _) => UpdateDeviceTypeSaveState();
        SaveDeviceTypeButton.Click += (_, _) => SaveDeviceType();
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
        SpecOs.Text = _agent.HostedOsDescription ?? "—";
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

    private void UpdateDeviceTypeSaveState() =>
        SaveDeviceTypeButton.IsEnabled = DeviceTypeInput.SelectedItem is DeviceTypeChoice;

    private void SaveDeviceType()
    {
        if (DeviceTypeInput.SelectedItem is not DeviceTypeChoice choice)
            return;

        _agent.UpdateSettings(settings => settings.DeviceType = choice.Type);
    }

    private void OnAgentChanged() => Dispatcher.UIThread.Post(Refresh);
    private void OnUpdateChanged() => Dispatcher.UIThread.Post(RefreshUpdate);

    private void Refresh()
    {
        StatusText.Text = _agent.StatusLine;
        DeviceText.Text = _agent.IsConnected
            ? $"Device: {DeviceName()}"
            : "Sign in to connect this device.";
        SyncButton.IsEnabled = _agent.IsConnected && _agent.IsSetupComplete && !_syncInProgress;
        ConnectButton.IsEnabled = _agent.IsSetupComplete;
        RefreshDeviceTypeSetup();
        HistoryList.ItemsSource = _agent.History;
        RefreshAccount();
    }

    private void RefreshDeviceTypeSetup()
    {
        if (_agent.IsSetupComplete)
        {
            DeviceTypeSetupText.Text = $"Currently: {DeviceTypeSetup.LabelFor(_agent.Settings.DeviceType!.Value)}. Change it here when this device changes.";
        }
        else if (_agent.SuggestedDeviceType == DeviceType.SteamDeck)
        {
            DeviceTypeSetupText.Text = "We detected a Steam Deck. Confirm it, or choose a different type, before connecting or syncing.";
        }
        else
        {
            DeviceTypeSetupText.Text = "Choose what kind of device this is before connecting or syncing. Other / not sure is a valid choice.";
        }
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

        AccountName.Text = DeviceName();
        AccountPlan.Text = "Connected (upload-only)";
        ThisDeviceText.Text = $"This device: {DeviceName()}";
        // TODO(dashboard): link directly to the device-management page once the dashboard UI ships.
        DevicesSection.IsVisible = true;
    }

    /// <summary>Builds, shows, and sends one exact hosted body after explicit confirmation.</summary>
    public async Task PreviewAndSyncAsync()
    {
        if (_syncInProgress)
            return;
        _syncInProgress = true;
        SyncButton.IsEnabled = false;
        try
        {
            PreparedSync? prepared = await _agent.PrepareSyncAsync();
            if (prepared is null)
                return;

            var preview = new UploadPreviewWindow(prepared);
            bool confirmed = await preview.ShowDialog<bool>(this);
            if (confirmed)
                await _agent.SyncNowAsync(prepared);
        }
        finally
        {
            _syncInProgress = false;
            SyncButton.IsEnabled = _agent.IsConnected && _agent.IsSetupComplete;
        }
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

    private string DeviceName() => string.IsNullOrWhiteSpace(_agent.Settings.DeviceName)
        ? DeviceIdentity.DefaultDeviceName
        : _agent.Settings.DeviceName;

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
        if (e.CloseReason == WindowCloseReason.ApplicationShutdown)
        {
            base.OnClosing(e);
            return;
        }

        if (!_agent.IsSetupComplete)
        {
            e.Cancel = true;
            return;
        }

        // Hide to the tray instead of quitting; the agent keeps running in the background.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
