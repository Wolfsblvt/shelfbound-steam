using Shelfbound.Core.Model;
using Shelfbound.Core.UserData;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class UserDataActionsTests
{
    [Fact]
    public void Record_first_seen_sets_baseline_once_and_only_timestamps_new_apps()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var firstScan = DateTimeOffset.UtcNow.AddDays(-10);

        UserDataActions.RecordFirstSeen(profile, [1, 2], firstScan, LibraryScope.FullLibrary);

        // First call establishes the baseline and marks all current apps as seen then.
        profile.FirstScanAt.ShouldBe(firstScan);
        profile.FirstSeen[1].ShouldBe(firstScan);
        profile.FirstSeen[2].ShouldBe(firstScan);

        var laterScan = DateTimeOffset.UtcNow;
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3], laterScan, LibraryScope.FullLibrary);

        // Baseline is sticky; existing apps keep their original timestamp; only app 3 is new. Scope was
        // stable (full → full), so app 3 is a genuine acquisition and dated at the later scan.
        profile.FirstScanAt.ShouldBe(firstScan);
        profile.FirstSeen[1].ShouldBe(firstScan);
        profile.FirstSeen[3].ShouldBe(laterScan);
    }

    [Fact]
    public void Scope_expansion_baselines_newly_visible_apps_instead_of_dating_them()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var baseline = DateTimeOffset.UtcNow.AddDays(-10);
        var fullScan = DateTimeOffset.UtcNow.AddDays(-2);

        // Baseline is an installed-only scan: it only sees the installed games (1, 2).
        UserDataActions.RecordFirstSeen(profile, [1, 2], baseline, LibraryScope.InstalledOnly);
        profile.WidestScanScope.ShouldBe(LibraryScope.InstalledOnly);

        // A later complete-source scan reveals not-installed games (3, 4). They may have been present
        // — newly visible, not newly added — so they're stamped at the baseline, not at the later scan.
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3, 4], fullScan, LibraryScope.FullLibrary);

        profile.FirstSeen[1].ShouldBe(baseline);
        profile.FirstSeen[3].ShouldBe(baseline);   // baselined, NOT fullScan
        profile.FirstSeen[4].ShouldBe(baseline);
        profile.WidestScanScope.ShouldBe(LibraryScope.FullLibrary);
    }

    [Fact]
    public void Genuine_acquisition_after_a_stable_full_baseline_is_dated_at_now()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var baseline = DateTimeOffset.UtcNow.AddDays(-10);
        var expandScan = DateTimeOffset.UtcNow.AddDays(-5);
        var additionScan = DateTimeOffset.UtcNow;

        UserDataActions.RecordFirstSeen(profile, [1, 2], baseline, LibraryScope.InstalledOnly);
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3], expandScan, LibraryScope.FullLibrary); // 3 baselined

        // Scope is now stable at fullLibrary; a genuinely new app under stable scope can be dated as an addition.
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3, 4], additionScan, LibraryScope.FullLibrary);

        profile.FirstSeen[3].ShouldBe(baseline);       // still baselined from the expansion
        profile.FirstSeen[4].ShouldBe(additionScan);   // genuine stable-scope addition — dated
    }

    [Fact]
    public void A_partial_scan_after_a_full_baseline_does_not_claim_an_acquisition()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var baseline = DateTimeOffset.UtcNow.AddDays(-10);
        var installScan = DateTimeOffset.UtcNow;

        UserDataActions.RecordFirstSeen(profile, [1, 2], baseline, LibraryScope.FullLibrary);

        // Installed presence alone does not prove when or how the game was acquired.
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3], installScan, LibraryScope.InstalledOnly);

        profile.FirstSeen[3].ShouldBe(baseline);
        profile.WidestScanScope.ShouldBe(LibraryScope.FullLibrary); // high-water mark never regresses
    }

    [Fact]
    public void Observed_subset_expands_installed_coverage_but_stable_partial_evidence_never_dates_apps()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var baseline = DateTimeOffset.UtcNow.AddDays(-10);
        var observedScan = DateTimeOffset.UtcNow.AddDays(-5);
        var laterObservedScan = DateTimeOffset.UtcNow;

        UserDataActions.RecordFirstSeen(profile, [1], baseline, LibraryScope.InstalledOnly);
        UserDataActions.RecordFirstSeen(profile, [1, 2], observedScan, LibraryScope.ObservedSubset);

        profile.FirstSeen[2].ShouldBe(baseline);
        profile.WidestScanScope.ShouldBe(LibraryScope.ObservedSubset);

        UserDataActions.RecordFirstSeen(profile, [1, 2, 3], laterObservedScan, LibraryScope.ObservedSubset);

        profile.FirstSeen[3].ShouldBe(baseline);
        profile.WidestScanScope.ShouldBe(LibraryScope.ObservedSubset);
    }

    [Fact]
    public void Full_library_is_broader_than_observed_subset_despite_its_preserved_lower_ordinal()
    {
        var profile = new UserProfile { OwnerId = "x" };
        var baseline = DateTimeOffset.UtcNow.AddDays(-10);
        var observedScan = DateTimeOffset.UtcNow.AddDays(-5);
        var fullScan = DateTimeOffset.UtcNow;

        UserDataActions.RecordFirstSeen(profile, [1], baseline, LibraryScope.InstalledOnly);
        UserDataActions.RecordFirstSeen(profile, [1, 2], observedScan, LibraryScope.ObservedSubset);
        UserDataActions.RecordFirstSeen(profile, [1, 2, 3], fullScan, LibraryScope.FullLibrary);

        profile.FirstSeen[3].ShouldBe(baseline);
        profile.WidestScanScope.ShouldBe(LibraryScope.FullLibrary);
    }

    [Fact]
    public void Profile_json_round_trips_observed_subset_and_legacy_full_library_names()
    {
        var observed = new UserProfile
        {
            OwnerId = "observed",
            WidestScanScope = LibraryScope.ObservedSubset,
        };

        string json = UserDataJson.Serialize(observed);

        json.ShouldContain("\"widestScanScope\":\"observedSubset\"");
        UserDataJson.Deserialize(json).WidestScanScope.ShouldBe(LibraryScope.ObservedSubset);
        UserDataJson.Deserialize("{\"ownerId\":\"legacy\",\"widestScanScope\":\"fullLibrary\"}")
            .WidestScanScope.ShouldBe(LibraryScope.FullLibrary);
    }

    [Fact]
    public void Reset_recency_baseline_clears_scan_time_first_seen_and_scope()
    {
        var profile = new UserProfile { OwnerId = "x" };
        UserDataActions.RecordFirstSeen(profile, [1, 2], DateTimeOffset.UtcNow.AddDays(-3), LibraryScope.FullLibrary);

        UserDataActions.ResetRecencyBaseline(profile);

        profile.FirstScanAt.ShouldBeNull();
        profile.FirstSeen.ShouldBeEmpty();
        profile.WidestScanScope.ShouldBe(LibraryScope.InstalledOnly);
    }
}
