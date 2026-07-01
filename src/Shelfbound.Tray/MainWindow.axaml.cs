using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Shelfbound.Tray;

/// <summary>A device row as shown in the account card: a title (with a "this device" marker) and a detail.</summary>
public sealed record DeviceRow(string Title, string Detail);

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
        _loading = false;
    }

    private void WireEvents()
    {
        SyncButton.Click += async (_, _) => await _agent.SyncNowAsync();
        ConnectButton.Click += async (_, _) => await _agent.ConnectAsync();
        SignOutButton.Click += async (_, _) => await _agent.SignOutAsync();
        UpdateRestartButton.Click += (_, _) => _update?.ApplyAndRestart();
        AutoSyncCheck.IsCheckedChanged += (_, _) => Apply();
        StartLoginCheck.IsCheckedChanged += (_, _) => Apply();
        StartMinimizedCheck.IsCheckedChanged += (_, _) => Apply();
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
        });
    }

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

        AccountName.Text = _agent.Account?.DisplayName
            ?? (_agent.Account is { } a ? ShortId(a.AccountId) : "Signed in");
        AccountPlan.Text = _agent.Entitlements is { } e
            ? $"{e.Plan} plan · up to {e.MaxDevices} {(e.MaxDevices == 1 ? "device" : "devices")}"
            : _agent.Account is null ? "Account details unavailable" : "Loading plan…";

        Guid? currentId = _agent.CurrentDevice?.Id;
        var rows = _agent.Devices
            .Select(d => new DeviceRow(
                d.Name + (d.Id == currentId ? "  (this device)" : ""),
                d.LastUsedAt is { } lu ? $"synced {lu.LocalDateTime:MMM d}" : "not synced yet"))
            .ToList();
        DevicesList.ItemsSource = rows;
        DevicesSection.IsVisible = rows.Count > 0;
    }

    private void RefreshUpdate()
    {
        if (_update is null)
        {
            UpdateBanner.IsVisible = false;
            return;
        }

        switch (_update.State)
        {
            case UpdateState.Downloading:
                UpdateBanner.IsVisible = true;
                UpdateText.Text = $"Downloading update {_update.TargetVersion}…";
                UpdateRestartButton.IsVisible = false;
                break;
            case UpdateState.ReadyToRestart:
                UpdateBanner.IsVisible = true;
                UpdateText.Text = $"Update {_update.TargetVersion} is ready.";
                UpdateRestartButton.IsVisible = true;
                break;
            default:
                UpdateBanner.IsVisible = false;
                break;
        }
    }

    // A short, human-friendly stand-in when no display name is available (opaque account ids can be long).
    private static string ShortId(string accountId) =>
        accountId.Length <= 12 ? accountId : $"Account {accountId[..8]}";

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Hide to the tray instead of quitting; the agent keeps running in the background.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
