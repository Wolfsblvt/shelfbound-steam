using Shelfbound.Client;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Tray;

/// <summary>
/// The non-UI heart of the agent: holds settings, runs syncs (scan + upload via the shared client),
/// schedules auto-sync, and keeps a short activity log. Raises <see cref="Changed"/> on any state change
/// so the window can refresh. Deliberately light: a timer plus an HttpClient, no background hogging.
/// </summary>
public sealed class SyncAgent : IDisposable
{
    private readonly object _lock = new();
    private readonly List<string> _history = [];
    private Timer? _timer;
    private string? _token;

    public AppSettings Settings { get; }
    public DeviceSpecs Specs { get; }
    public bool IsConnected => !string.IsNullOrEmpty(_token);
    public DateTimeOffset? LastSync { get; private set; }
    public string StatusLine { get; private set; } = "Starting…";
    public IReadOnlyList<string> History
    {
        get { lock (_lock) { return _history.ToList(); } }
    }

    public event Action? Changed;

    public SyncAgent()
    {
        Settings = AppSettings.Load();
        _token = TokenStore.Load();
        Specs = HardwareInfo.Collect();
    }

    public void Start()
    {
        Reschedule(syncImmediately: true);
    }

    /// <summary>Applies a settings change, persists it, updates auto-start, and reschedules.</summary>
    public void UpdateSettings(Action<AppSettings> mutate)
    {
        mutate(Settings);
        Settings.Save();
        AutoStart.Apply(Settings.StartOnLogin);
        Reschedule();
    }

    public async Task SyncNowAsync()
    {
        if (!IsConnected)
        {
            Log("Not connected — connect your account first.");
            return;
        }

        Log("Syncing…");
        try
        {
            string deviceName = DeviceIdentity.NormalizeName(Settings.DeviceName);
            SnapshotBuildResult build = await SnapshotBuilder.BuildAsync(new SnapshotBuildOptions
            {
                ToolVersion = AppInfo.Version,
                DeviceName = deviceName,
            });
            using var client = new ShelfboundClient(Settings.ServerUrl, _token!);
            UploadResult result = await client.UploadAsync(build.Snapshot);
            Log(result.Status switch
            {
                UploadStatus.Success => $"Synced {result.GameCount} games.",
                UploadStatus.Throttled => $"Skipped — {result.Message}",
                UploadStatus.Unauthorized => "Token rejected — reconnect your account.",
                UploadStatus.DeviceNameMismatch => "Device name mismatch — reconnect this device.",
                _ => $"Failed — {result.Message}",
            });
            if (result.Ok)
                LastSync = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            Log($"Error — {ex.Message}");
        }
        UpdateStatus();
    }

    public async Task ConnectAsync()
    {
        Log("Opening browser to connect…");
        try
        {
            string deviceName = DeviceIdentity.NormalizeName(Settings.DeviceName);
            ConnectFlowResult? connection = await ConnectFlow.RunAsync(
                Settings.WebAppUrl,
                Settings.ServerUrl,
                deviceName);
            if (connection is null)
            {
                Log("Connect cancelled or timed out.");
                return;
            }

            _token = TokenStore.Load();
            if (string.IsNullOrEmpty(_token))
            {
                TokenStore.Clear();
                Log("Connect failed — the device token could not be loaded from secure storage.");
                return;
            }

            try
            {
                Settings.DeviceName = connection.DeviceName;
                Settings.Save();
            }
            catch
            {
                TokenStore.Clear();
                _token = null;
                throw;
            }

            Log("Device connected (upload-only).");
            await SyncNowAsync();
            Reschedule();
        }
        catch (Exception ex)
        {
            Log($"Connect failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Signs this device out locally: clears the stored token and stops auto-sync. Reconnecting mints a
    /// fresh token. Server-side revoke requires a cookie session (M-4); tokens expire at 90 days or can
    /// be revoked from the web dashboard.
    /// </summary>
    public Task SignOutAsync()
    {
        TokenStore.Clear();
        _token = null;
        Log("Signed out.");
        Reschedule(); // IsConnected is now false → auto-sync timer is torn down.
        return Task.CompletedTask;
    }

    private void Reschedule(bool syncImmediately = false)
    {
        _timer?.Dispose();
        _timer = null;
        if (Settings.AutoSync && IsConnected)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, Settings.IntervalMinutes));
            TimeSpan dueTime = syncImmediately ? TimeSpan.Zero : interval;
            _timer = new Timer(_ => _ = SyncNowAsync(), null, dueTime, interval);
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusLine = !IsConnected ? "Not connected"
            : LastSync is null ? "Connected — not synced yet"
            : $"Last synced {Friendly(LastSync.Value)}";
        Changed?.Invoke();
    }

    private void Log(string message)
    {
        lock (_lock)
        {
            _history.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
            if (_history.Count > 100)
                _history.RemoveAt(_history.Count - 1);
        }
        Changed?.Invoke();
    }

    private static string Friendly(DateTimeOffset when)
    {
        TimeSpan span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        return when.ToString("yyyy-MM-dd HH:mm");
    }

    public void Dispose() => _timer?.Dispose();
}
