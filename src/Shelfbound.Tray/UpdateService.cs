using Velopack;
using Velopack.Sources;

namespace Shelfbound.Tray;

/// <summary>Where the self-updater is in its lifecycle; drives the update banner + tray menu item.</summary>
public enum UpdateState { Unsupported, UpToDate, Checking, Downloading, ReadyToRestart, Failed }

/// <summary>
/// Self-update against GitHub Releases via Velopack. Only active when the app was installed by the Velopack
/// installer (<see cref="UpdateManager.IsInstalled"/>); running from source / <c>dotnet run</c> is
/// <see cref="UpdateState.Unsupported"/>, so dev builds never try to update themselves.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(AppInfo.RepoUrl, accessToken: null, prerelease: false));
        State = _manager.IsInstalled ? UpdateState.UpToDate : UpdateState.Unsupported;
    }

    public UpdateState State { get; private set; }

    /// <summary>The version being offered/downloaded, when <see cref="State"/> is Downloading/ReadyToRestart.</summary>
    public string? TargetVersion { get; private set; }

    /// <summary>The GitHub Release page for the offered update's notes, or null when none is pending.</summary>
    public string? TargetReleaseUrl => TargetVersion is null ? null : AppInfo.ReleaseUrl(TargetVersion);

    public bool IsSupported => _manager.IsInstalled;

    public event Action? Changed;

    /// <summary>Checks GitHub for a newer release and downloads it in the background if one is found.</summary>
    public async Task CheckAsync()
    {
        if (!IsSupported || State is UpdateState.Checking or UpdateState.Downloading)
            return;

        try
        {
            Set(UpdateState.Checking, null);
            UpdateInfo? info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                Set(UpdateState.UpToDate, null);
                return;
            }

            Set(UpdateState.Downloading, info.TargetFullRelease.Version.ToString());
            await _manager.DownloadUpdatesAsync(info);
            _pending = info;
            Set(UpdateState.ReadyToRestart, info.TargetFullRelease.Version.ToString());
        }
        catch
        {
            // Update failures are non-fatal — the app keeps running on the current version.
            Set(UpdateState.Failed, null);
        }
    }

    /// <summary>Applies a downloaded update and restarts the app. No-op unless an update is ready.</summary>
    public void ApplyAndRestart()
    {
        if (State == UpdateState.ReadyToRestart && _pending is not null)
            _manager.ApplyUpdatesAndRestart(_pending);
    }

    private void Set(UpdateState state, string? version)
    {
        State = state;
        TargetVersion = version;
        Changed?.Invoke();
    }
}
