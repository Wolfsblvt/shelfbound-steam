using Avalonia.Controls;
using Avalonia.Threading;

namespace Shelfbound.Tray;

public partial class MainWindow : Window
{
    private readonly SyncAgent _agent;
    private bool _loading;

    public MainWindow(SyncAgent agent)
    {
        _agent = agent;
        InitializeComponent();
        LoadSettings();
        WireEvents();
        _agent.Changed += OnAgentChanged;
        Refresh();
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
        AutoSyncCheck.IsCheckedChanged += (_, _) => Apply();
        StartLoginCheck.IsCheckedChanged += (_, _) => Apply();
        StartMinimizedCheck.IsCheckedChanged += (_, _) => Apply();
        IntervalInput.ValueChanged += (_, _) => Apply();
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

    private void Refresh()
    {
        StatusText.Text = _agent.StatusLine;
        DeviceText.Text = _agent.IsConnected
            ? $"Device: {_agent.Settings.DeviceName ?? Environment.MachineName}"
            : "Sign in to connect this device.";
        HistoryList.ItemsSource = _agent.History;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Hide to the tray instead of quitting; the agent keeps running in the background.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
