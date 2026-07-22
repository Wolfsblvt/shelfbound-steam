using System.Globalization;
using System.Text.Json;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Vdf;

namespace Shelfbound.Steam.Steam;

/// <summary>The outcome of reading one account-scoped Steam Private-app cache.</summary>
internal enum PrivateAppsEvidenceState
{
    Positive,
    Absent,
    Empty,
    Unreadable,
    Malformed,
    AccountMismatch,
}

/// <summary>
/// One local cache outcome. App ids are populated only for positive evidence and remain local-only.
/// No outcome represents a complete or current negative.
/// </summary>
internal sealed record PrivateAppsEvidenceOutcome(
    PrivateAppsEvidenceState State,
    IReadOnlySet<int> PrivateAppIds);

/// <summary>The one permitted account-scoped cache scalar plus mismatch-only sibling evidence.</summary>
internal sealed record PrivateAppsCacheValue(string? RawValue, bool HasOtherAccountKey);

/// <summary>Same-device union of positive Private-app cache membership plus all source outcomes.</summary>
internal sealed record PrivateAppsEvidenceResult
{
    public required IReadOnlyList<PrivateAppsEvidenceOutcome> Outcomes { get; init; }
    public required IReadOnlySet<int> PrivateAppIds { get; init; }

    public string Describe()
    {
        int positiveAccounts = Outcomes.Count(outcome => outcome.State == PrivateAppsEvidenceState.Positive);
        int uncertainAccounts = Outcomes.Count - positiveAccounts;

        if (positiveAccounts == 0)
        {
            return Outcomes.Count == 0
                ? "No local Steam account was available for Private-game evidence. No games were omitted."
                : "Steam's local cache supplied no positive Private-game evidence. Missing, empty, unreadable, malformed, or account-mismatched cache data proves nothing, so no games were omitted from it.";
        }

        string uncertainty = uncertainAccounts > 0
            ? $" {uncertainAccounts} other local account cache(s) were inconclusive and did not authorize omission."
            : string.Empty;
        return $"Steam's local cache last marked {PrivateAppIds.Count} game(s) Private across {positiveAccounts} local account(s). The cache may be stale.{uncertainty}";
    }
}

/// <summary>
/// Reads the bounded <c>UserLocalConfigStore/WebStorage/PrivateApps_&lt;accountId&gt;</c> cache from
/// each known local Steam account. Only positive membership is unioned; every other state is
/// preserved as uncertainty.
/// </summary>
internal static class PrivateAppsEvidenceReader
{
    public static PrivateAppsEvidenceResult Read(
        string steamRoot,
        IReadOnlyList<SteamAccount> accounts) =>
        Read(steamRoot, accounts, PrivateAppsEvidenceReaderDependencies.Default);

    internal static PrivateAppsEvidenceResult Read(
        string steamRoot,
        IReadOnlyList<SteamAccount> accounts,
        PrivateAppsEvidenceReaderDependencies dependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamRoot);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(dependencies);

        var outcomes = new List<PrivateAppsEvidenceOutcome>(accounts.Count);
        var privateAppIds = new HashSet<int>();

        foreach (SteamAccount account in accounts)
        {
            PrivateAppsEvidenceOutcome outcome = ReadAccount(steamRoot, account, dependencies);
            outcomes.Add(outcome);
            if (outcome.State == PrivateAppsEvidenceState.Positive)
                privateAppIds.UnionWith(outcome.PrivateAppIds);
        }

        return new PrivateAppsEvidenceResult
        {
            Outcomes = outcomes,
            PrivateAppIds = privateAppIds,
        };
    }

    private static PrivateAppsEvidenceOutcome ReadAccount(
        string steamRoot,
        SteamAccount account,
        PrivateAppsEvidenceReaderDependencies dependencies)
    {
        if (account.AccountId is not long accountId || accountId <= 0 || accountId > uint.MaxValue)
            return Outcome(PrivateAppsEvidenceState.AccountMismatch);

        string accountText = accountId.ToString(CultureInfo.InvariantCulture);
        string path = Path.Combine(steamRoot, "userdata", accountText, "config", "localconfig.vdf");
        if (!dependencies.FileExists(path))
            return Outcome(PrivateAppsEvidenceState.Absent);

        string expectedKey = $"PrivateApps_{accountText}";
        PrivateAppsCacheValue cacheValue;
        try
        {
            cacheValue = dependencies.ReadCacheValue(path, expectedKey);
        }
        catch (UnauthorizedAccessException)
        {
            return Outcome(PrivateAppsEvidenceState.Unreadable);
        }
        catch (IOException)
        {
            return Outcome(PrivateAppsEvidenceState.Unreadable);
        }
        catch (FormatException)
        {
            return Outcome(PrivateAppsEvidenceState.Malformed);
        }

        string? rawValue = cacheValue.RawValue;
        if (rawValue is null)
        {
            return Outcome(cacheValue.HasOtherAccountKey
                ? PrivateAppsEvidenceState.AccountMismatch
                : PrivateAppsEvidenceState.Absent);
        }

        if (string.IsNullOrWhiteSpace(rawValue))
            return Outcome(PrivateAppsEvidenceState.Empty);

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawValue, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Outcome(PrivateAppsEvidenceState.Malformed);
            if (document.RootElement.GetArrayLength() > SteamInputLimits.MaxPrivateAppEntries)
                return Outcome(PrivateAppsEvidenceState.Malformed);

            var appIds = new HashSet<int>();
            foreach (JsonElement value in document.RootElement.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out int appId) ||
                    appId <= 0)
                {
                    return Outcome(PrivateAppsEvidenceState.Malformed);
                }
                appIds.Add(appId);
            }

            return appIds.Count == 0
                ? Outcome(PrivateAppsEvidenceState.Empty)
                : new PrivateAppsEvidenceOutcome(PrivateAppsEvidenceState.Positive, appIds);
        }
        catch (JsonException)
        {
            return Outcome(PrivateAppsEvidenceState.Malformed);
        }
    }

    private static PrivateAppsEvidenceOutcome Outcome(PrivateAppsEvidenceState state) =>
        new(state, new HashSet<int>());
}

internal sealed record PrivateAppsEvidenceReaderDependencies
{
    private static readonly string[] PrivateAppsObjectPath = ["UserLocalConfigStore", "WebStorage"];

    public required Func<string, bool> FileExists { get; init; }
    public required Func<string, string, PrivateAppsCacheValue> ReadCacheValue { get; init; }

    public static PrivateAppsEvidenceReaderDependencies Default { get; } = new()
    {
        FileExists = File.Exists,
        ReadCacheValue = (path, expectedKey) =>
        {
            VdfScalarSelection selection = VdfParser.SelectFileValue(
                path,
                PrivateAppsObjectPath,
                expectedKey,
                "PrivateApps_");
            return new PrivateAppsCacheValue(selection.Value, selection.HasMatchingSibling);
        },
    };
}
