using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Client;

/// <summary>A local-only summary shown when one game would be omitted from a hosted upload.</summary>
internal sealed record SkippedPrivateGame(int AppId, string Name);

/// <summary>Local-only status for the best-effort Private-game exclusion preparation.</summary>
internal sealed record PrivateGameExclusionStatus(
    bool Enabled,
    bool HasPositiveEvidence,
    bool FailedOpen,
    int OmittedGameCount,
    string Message);

/// <summary>
/// One canonical hosted body plus the local-only evidence and preview summary used to prepare it.
/// Source identifiers and positive membership are retained only in memory and are never serialized.
/// </summary>
internal sealed class PrivateGameUploadPreparation
{
    private readonly SnapshotDocument _localSnapshot;
    private readonly IReadOnlySet<int> _positivePrivateAppIds;
    private readonly string _evidenceDescription;
    private readonly bool _failedOpen;
    private readonly string _machineName;
    private readonly bool _enabled;

    internal PrivateGameUploadPreparation(
        SnapshotDocument localSnapshot,
        IReadOnlySet<int> positivePrivateAppIds,
        IReadOnlySet<int> unskippedAppIds,
        string evidenceDescription,
        bool failedOpen,
        string machineName,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(localSnapshot);
        ArgumentNullException.ThrowIfNull(positivePrivateAppIds);
        ArgumentNullException.ThrowIfNull(unskippedAppIds);

        _localSnapshot = localSnapshot;
        _positivePrivateAppIds = positivePrivateAppIds.Where(appId => appId > 0).ToHashSet();
        _evidenceDescription = evidenceDescription;
        _failedOpen = failedOpen;
        _machineName = machineName;
        _enabled = enabled;

        IReadOnlySet<int> normalizedOverrides = NormalizeAppIds(unskippedAppIds);
        var excludedAppIds = _positivePrivateAppIds
            .Where(appId => !normalizedOverrides.Contains(appId))
            .ToHashSet();
        Upload = HostedProjection.Prepare(localSnapshot, machineName, excludedAppIds);
        SkippedGames = localSnapshot.Games
            .Where(game => excludedAppIds.Contains(game.AppId))
            .GroupBy(game => game.AppId)
            .Select(group => new SkippedPrivateGame(group.Key, group.First().Name))
            .ToArray();

        string omission = enabled && SkippedGames.Count > 0
            ? $" {SkippedGames.Count} matching game(s) will be omitted from this hosted body."
            : enabled && _positivePrivateAppIds.Count > 0
                ? " No matching game will be omitted after device-local overrides."
                : string.Empty;
        Status = new PrivateGameExclusionStatus(
            Enabled: enabled,
            HasPositiveEvidence: _positivePrivateAppIds.Count > 0,
            FailedOpen: failedOpen,
            OmittedGameCount: SkippedGames.Count,
            Message: evidenceDescription + omission);
    }

    public HostedUpload Upload { get; }
    public IReadOnlyList<SkippedPrivateGame> SkippedGames { get; }
    public PrivateGameExclusionStatus Status { get; }

    /// <summary>Regenerates canonical bytes from the same scan/evidence after local overrides change.</summary>
    public PrivateGameUploadPreparation WithUnskippedAppIds(IReadOnlySet<int> unskippedAppIds) =>
        new(
            _localSnapshot,
            _positivePrivateAppIds,
            NormalizeAppIds(unskippedAppIds),
            _evidenceDescription,
            _failedOpen,
            _machineName,
            _enabled);

    internal static PrivateGameUploadPreparation Disabled(SnapshotDocument snapshot, string machineName) =>
        new(
            snapshot,
            new HashSet<int>(),
            new HashSet<int>(),
            "Private-game exclusion is off.",
            failedOpen: false,
            machineName,
            enabled: false);

    private static IReadOnlySet<int> NormalizeAppIds(IReadOnlySet<int> appIds)
    {
        ArgumentNullException.ThrowIfNull(appIds);
        return appIds.Where(appId => appId > 0).ToHashSet();
    }
}

/// <summary>Builds the best-effort local Private-game exclusion preparation used by hosted clients.</summary>
internal static class PrivateGameUploadPreparer
{
    public static PrivateGameUploadPreparation Prepare(
        SnapshotDocument snapshot,
        bool enabled,
        IReadOnlySet<int> unskippedAppIds,
        string? steamPath = null) =>
        Prepare(snapshot, enabled, unskippedAppIds, steamPath, Environment.MachineName);

    internal static PrivateGameUploadPreparation Prepare(
        SnapshotDocument snapshot,
        bool enabled,
        IReadOnlySet<int> unskippedAppIds,
        string? steamPath,
        string machineName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(unskippedAppIds);

        if (!enabled)
            return PrivateGameUploadPreparation.Disabled(snapshot, machineName);

        string? steamRoot = SteamInstallLocator.Locate(steamPath);
        if (steamRoot is null)
        {
            return new PrivateGameUploadPreparation(
                snapshot,
                new HashSet<int>(),
                unskippedAppIds,
                "Steam's local Private-game cache was unavailable. No games were omitted.",
                failedOpen: true,
                machineName);
        }

        PrivateAppsEvidenceResult evidence = PrivateAppsEvidenceReader.Read(
            steamRoot,
            snapshot.SteamAccounts);
        bool failedOpen = evidence.Outcomes.Count == 0 ||
            evidence.Outcomes.Any(outcome => outcome.State != PrivateAppsEvidenceState.Positive);
        return new PrivateGameUploadPreparation(
            snapshot,
            evidence.PrivateAppIds,
            unskippedAppIds,
            evidence.Describe(),
            failedOpen,
            machineName);
    }

    internal static PrivateGameUploadPreparation PrepareFromEvidence(
        SnapshotDocument snapshot,
        IReadOnlySet<int> positivePrivateAppIds,
        IReadOnlySet<int> unskippedAppIds,
        string evidenceDescription = "Synthetic positive local cache evidence.",
        bool failedOpen = false,
        string machineName = "synthetic-host") =>
        new(
            snapshot,
            positivePrivateAppIds,
            unskippedAppIds,
            evidenceDescription,
            failedOpen,
            machineName);
}
