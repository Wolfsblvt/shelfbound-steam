using Shelfbound.Client;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Tray;

/// <summary>A locally built upload whose exact JSON can be previewed and then sent without rebuilding.</summary>
public sealed record PreparedSync(HostedUpload Upload, IReadOnlyList<string> Warnings);

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
    public string? HostedOsDescription { get; }
    public bool IsConnected => !string.IsNullOrEmpty(_token);
    public DateTimeOffset? LastSync { get; private set; }
    public string StatusLine { get; private set; } = "Starting…";
    public IReadOnlyList<string> History
    {
        get { lock (_lock) { return _history.ToList(); } }
    }

    // Signed-in account context, refreshed from the server via the connected token. All optional: the tray
    // works offline / pre-deploy, so these stay null/empty until a successful RefreshAccountAsync.
    public AccountInfo? Account { get; private set; }
    public Entitlements? Entitlements { get; private set; }

    public event Action? Changed;

    public SyncAgent()
    {
        Settings = AppSettings.Load();
        _token = TokenStore.Load();
        SnapshotDevice device = DeviceIdentity.Resolve(Settings.DeviceName, null);
        Specs = device.Specs ?? new DeviceSpecs();
        HostedOsDescription = HostedProjection.CoarsenOsDescription(device.Os, Specs.OsDescription);
    }

    public void Start()
    {
        Reschedule();
        if (IsConnected)
            _ = RefreshAccountAsync();
    }

    /// <summary>Applies a settings change, persists it, updates auto-start, and reschedules.</summary>
    public void UpdateSettings(Action<AppSettings> mutate)
    {
        mutate(Settings);
        Settings.Save();
        AutoStart.Apply(Settings.StartOnLogin);
        Reschedule();
    }

    /// <summary>Builds the one exact hosted body that the tray must show before a manual sync.</summary>
    public Task<PreparedSync?> PrepareSyncAsync() => BuildPreparedSyncAsync(logPreview: true);

    private async Task<PreparedSync?> BuildPreparedSyncAsync(bool logPreview)
    {
        if (!IsConnected)
        {
            Log("Not connected — connect your account first.");
            return null;
        }

        if (logPreview)
            Log("Preparing upload preview…");
        try
        {
            SnapshotBuildResult build = await SnapshotBuilder.BuildAsync(new SnapshotBuildOptions
            {
                ToolVersion = AppInfo.Version,
                DeviceName = Settings.DeviceName,
            });
            return new PreparedSync(HostedProjection.Prepare(build.Snapshot), build.Warnings);
        }
        catch (Exception ex)
        {
            Log($"Preview failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>Sends a user-confirmed prepared body. No scan or re-serialization happens here.</summary>
    public async Task SyncNowAsync(PreparedSync prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        UploadResult result = await UploadPreparedAsync(prepared);
        if (result.Ok)
        {
            Settings.HostedUploadConsentVersion = HostedProjection.ProjectionVersion;
            Settings.Save();
            Reschedule(startImmediately: false);
        }
    }

    public async Task ConnectAsync()
    {
        Log("Opening browser to connect…");
        try
        {
            string device = string.IsNullOrWhiteSpace(Settings.DeviceName)
                ? DeviceIdentity.DefaultDeviceName
                : Settings.DeviceName.Trim();
            string? token = await ConnectFlow.RunAsync(Settings.WebAppUrl, device);
            if (token is null)
            {
                Log("Connect cancelled or timed out.");
                return;
            }
            _token = token;
            TokenStore.Save(token);
            if (string.IsNullOrWhiteSpace(Settings.DeviceName))
                Settings.DeviceName = device;
            Settings.Save();
            Log("Account connected.");
            Reschedule();
            if (!HasUploadConsent)
                Log("Review and confirm 'Sync now' before background sync can start.");
            await RefreshAccountAsync();
        }
        catch (Exception ex)
        {
            Log($"Connect failed — {ex.Message}");
        }
    }

    /// <summary>Pulls the signed-in account and plan entitlements for the account card.</summary>
    public async Task RefreshAccountAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            Account = null;
            Entitlements = null;
            Changed?.Invoke();
            return;
        }

        try
        {
            using var client = new ShelfboundClient(Settings.ServerUrl, _token!);
            // Independent reads — run them together; each swallows its own failure and returns null.
            Task<AccountInfo?> account = client.GetAccountAsync(ct);
            Task<Entitlements?> entitlements = client.GetEntitlementsAsync(ct);
            await Task.WhenAll(account, entitlements);
            Account = account.Result;
            Entitlements = entitlements.Result;
        }
        catch (Exception ex)
        {
            Log($"Couldn't load account — {ex.Message}");
        }
        Changed?.Invoke();
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
        Account = null;
        Entitlements = null;
        Log("Signed out.");
        Reschedule(); // IsConnected is now false → auto-sync timer is torn down.
        return Task.CompletedTask;
    }

    private bool HasUploadConsent =>
        Settings.HostedUploadConsentVersion == HostedProjection.ProjectionVersion;

    private void Reschedule(bool startImmediately = true)
    {
        _timer?.Dispose();
        _timer = null;
        if (Settings.AutoSync && IsConnected && HasUploadConsent)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, Settings.IntervalMinutes));
            TimeSpan dueTime = startImmediately ? TimeSpan.Zero : interval;
            _timer = new Timer(_ => _ = SyncAutomaticallyAsync(), null, dueTime, interval);
        }
        UpdateStatus();
    }

    private async Task SyncAutomaticallyAsync()
    {
        PreparedSync? prepared = await BuildPreparedSyncAsync(logPreview: false);
        if (prepared is not null)
            await UploadPreparedAsync(prepared);
    }

    private async Task<UploadResult> UploadPreparedAsync(PreparedSync prepared)
    {
        if (!IsConnected)
        {
            Log("Not connected — connect your account first.");
            return new UploadResult
            {
                Status = UploadStatus.Unauthorized,
                ErrorCode = UploadErrorCode.Unauthorized,
                Message = "Not connected.",
            };
        }

        Log("Syncing approved hosted body…");
        using var client = new ShelfboundClient(Settings.ServerUrl, _token!);
        UploadResult result = await client.UploadAsync(prepared.Upload);
        Log(Describe(result));
        if (!string.IsNullOrWhiteSpace(result.Warning))
            Log($"Server warning — {result.Warning}");
        foreach (string warning in prepared.Warnings.Take(3))
            Log($"Scan warning — {warning}");
        if (prepared.Warnings.Count > 3)
            Log($"Scan warning — … and {prepared.Warnings.Count - 3} more.");
        if (result.Ok)
            LastSync = DateTimeOffset.Now;
        UpdateStatus();
        return result;
    }

    private void UpdateStatus()
    {
        StatusLine = !IsConnected ? "Not connected"
            : Settings.AutoSync && !HasUploadConsent ? "Connected — review an upload to enable auto-sync"
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

    private static string Describe(UploadResult result) => result.Status switch
    {
        UploadStatus.Success => $"Synced {result.GameCount} games.",
        UploadStatus.Throttled => $"Skipped ({result.ErrorCode}) — {result.Message}",
        UploadStatus.Unauthorized => "Token rejected — reconnect your account.",
        UploadStatus.Forbidden => $"Forbidden ({result.ErrorCode}) — {result.Message}",
        UploadStatus.DeviceLimited => $"Device limit reached — {result.Message}",
        UploadStatus.InvalidSnapshot => $"Invalid hosted snapshot — {result.Message}",
        UploadStatus.PayloadTooLarge => $"Hosted snapshot too large — {result.Message}",
        _ => $"Failed ({result.ErrorCode}) — {result.Message}",
    };

    public void Dispose() => _timer?.Dispose();
}
