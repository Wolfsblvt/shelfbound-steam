using System.Text.Json;
using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;
using Shelfbound.Steam.Vdf;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public sealed class PrivateAppsEvidenceReaderTests
{
    private const long SteamId64Base = 76561197960265728L;

    [Fact]
    public void Shared_fixture_covers_positive_and_every_ambiguous_outcome()
    {
        EvidenceFixtureRoot fixture = JsonSerializer.Deserialize<EvidenceFixtureRoot>(
            File.ReadAllText(FixturePath("private-game-exclusion.cases.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        foreach (EvidenceFixture evidenceCase in fixture.EvidenceCases)
        {
            const string steamRoot = "synthetic-steam-root";
            var inputsByPath = evidenceCase.Accounts.ToDictionary(
                account => LocalConfigPath(steamRoot, account.AccountId),
                StringComparer.OrdinalIgnoreCase);
            var accounts = evidenceCase.Accounts
                .Select(account => new SteamAccount
                {
                    SteamId64 = (SteamId64Base + account.AccountId).ToString(),
                })
                .ToArray();
            var dependencies = new PrivateAppsEvidenceReaderDependencies
            {
                FileExists = path => inputsByPath[path].Action != "missing",
                ParseFile = path => ParseFixture(inputsByPath[path]),
            };

            PrivateAppsEvidenceResult result = PrivateAppsEvidenceReader.Read(
                steamRoot,
                accounts,
                dependencies);

            PrivateAppsEvidenceState[] expectedStates = evidenceCase.ExpectedStates
                .Select(state => Enum.Parse<PrivateAppsEvidenceState>(state, ignoreCase: true))
                .ToArray();
            result.Outcomes.Select(outcome => outcome.State).ShouldBe(expectedStates);
            result.PrivateAppIds.Order().ShouldBe(evidenceCase.ExpectedPrivateAppIds.Order());
            foreach (EvidenceAccountFixture account in evidenceCase.Accounts)
                result.Describe().ShouldNotContain(account.AccountId.ToString());
            foreach (int appId in evidenceCase.ExpectedPrivateAppIds)
                result.Describe().ShouldNotContain(appId.ToString());
        }
    }

    [Fact]
    public void Reads_only_the_documented_account_scoped_localconfig_path()
    {
        string root = Path.Combine(Path.GetTempPath(), $"shelfbound-private-fixture-{Guid.NewGuid():N}");
        try
        {
            string path = LocalConfigPath(root, 10);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "\"UserLocalConfigStore\" { \"WebStorage\" { \"PrivateApps_10\" \"[20]\" } }");
            var account = new SteamAccount { SteamId64 = (SteamId64Base + 10).ToString() };

            PrivateAppsEvidenceResult result = PrivateAppsEvidenceReader.Read(root, [account]);

            result.Outcomes.ShouldHaveSingleItem().State.ShouldBe(PrivateAppsEvidenceState.Positive);
            result.PrivateAppIds.ShouldBe(new HashSet<int> { 20 });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* test cleanup only */ }
        }
    }

    [Fact]
    public void Rejects_an_unbounded_private_app_array()
    {
        string oversizedArray = $"[{string.Join(',', Enumerable.Repeat(1, SteamInputLimits.MaxPrivateAppEntries + 1))}]";
        string vdf = $"\"UserLocalConfigStore\" {{ \"WebStorage\" {{ \"PrivateApps_10\" \"{oversizedArray}\" }} }}";
        var account = new SteamAccount { SteamId64 = (SteamId64Base + 10).ToString() };
        var dependencies = new PrivateAppsEvidenceReaderDependencies
        {
            FileExists = _ => true,
            ParseFile = _ => VdfParser.Parse(vdf),
        };

        PrivateAppsEvidenceResult result = PrivateAppsEvidenceReader.Read("fixture", [account], dependencies);

        result.Outcomes.ShouldHaveSingleItem().State.ShouldBe(PrivateAppsEvidenceState.Malformed);
        result.PrivateAppIds.ShouldBeEmpty();
    }

    private static VdfObject ParseFixture(EvidenceAccountFixture account) => account.Action switch
    {
        "read" => VdfParser.Parse(account.Vdf ?? throw new InvalidDataException("Fixture VDF is required.")),
        "unreadable" => throw new IOException("Synthetic unreadable fixture."),
        _ => throw new InvalidDataException($"Unexpected fixture action '{account.Action}'."),
    };

    private static string LocalConfigPath(string steamRoot, int accountId) =>
        Path.Combine(steamRoot, "userdata", accountId.ToString(), "config", "localconfig.vdf");

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private sealed record EvidenceFixtureRoot(IReadOnlyList<EvidenceFixture> EvidenceCases);
    private sealed record EvidenceFixture(
        string Name,
        IReadOnlyList<EvidenceAccountFixture> Accounts,
        IReadOnlyList<string> ExpectedStates,
        IReadOnlyList<int> ExpectedPrivateAppIds);
    private sealed record EvidenceAccountFixture(int AccountId, string Action, string? Vdf);
}
