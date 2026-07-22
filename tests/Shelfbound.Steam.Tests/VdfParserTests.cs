using Shelfbound.Steam.Vdf;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class VdfParserTests
{
    [Fact]
    public void Parses_nested_objects_and_values()
    {
        const string vdf = """
            "AppState"
            {
                "appid"  "228980"
                "name"   "Steamworks Common Redistributables"
                "InstalledDepots"
                {
                    "228981" { "size" "5884085" }
                }
            }
            """;

        var state = VdfParser.Parse(vdf).GetObject("AppState");

        state.ShouldNotBeNull();
        state!.GetValue("appid").ShouldBe("228980");
        state.GetValue("name").ShouldBe("Steamworks Common Redistributables");
        state.GetObject("InstalledDepots")!.GetObject("228981")!.GetValue("size").ShouldBe("5884085");
    }

    [Fact]
    public void Key_lookups_are_case_insensitive()
    {
        var root = VdfParser.Parse("""
            "Root" { "Key" "value" }
            """);

        root.GetObject("root")!.GetValue("KEY").ShouldBe("value");
    }

    [Fact]
    public void Handles_escapes_and_line_comments()
    {
        const string vdf = """
            "root"
            {
                // a comment line
                "path" "C:\\Program Files (x86)\\Steam"
                "quote" "a\"b"
            }
            """;

        var root = VdfParser.Parse(vdf).GetObject("root")!;

        root.GetValue("path").ShouldBe(@"C:\Program Files (x86)\Steam");
        root.GetValue("quote").ShouldBe("a\"b");
    }

    [Fact]
    public void Throws_on_unterminated_object()
    {
        Should.Throw<FormatException>(() => VdfParser.Parse("\"root\" { \"k\" \"v\" "));
    }

    [Fact]
    public void Rejects_oversized_input()
    {
        string oversized = new('x', SteamInputLimits.MaxVdfTextChars + 1);

        FormatException error = Should.Throw<FormatException>(() => VdfParser.Parse(oversized));

        error.Message.ShouldContain("character limit");
    }

    [Fact]
    public void Rejects_pathological_nesting()
    {
        string nested = string.Concat(Enumerable.Repeat("\"k\" { ", SteamInputLimits.MaxVdfDepth + 1)) +
            string.Concat(Enumerable.Repeat(" }", SteamInputLimits.MaxVdfDepth + 1));

        FormatException error = Should.Throw<FormatException>(() => VdfParser.Parse(nested));

        error.Message.ShouldContain("depth limit");
    }

    [Fact]
    public void Selects_only_the_requested_scalar_and_reports_matching_siblings()
    {
        const string vdf = """
            "UnrelatedRoot" { "UnrelatedScalar" "must-not-be-selected" }
            "UserLocalConfigStore"
            {
                "WebStorage"
                {
                    "UnrelatedScalar" "must-not-be-selected"
                    "PrivateApps_11" "[40]"
                    "PrivateApps_10" "[20]"
                }
            }
            """;

        VdfScalarSelection selection = VdfParser.SelectValue(
            vdf,
            ["UserLocalConfigStore", "WebStorage"],
            "PrivateApps_10",
            "PrivateApps_");

        selection.Value.ShouldBe("[20]");
        selection.HasMatchingSibling.ShouldBeTrue();
        selection.ToString().ShouldNotContain("must-not-be-selected");
    }

    [Fact]
    public void Selective_reader_enforces_depth_while_skipping_unrelated_subtrees()
    {
        string nested = string.Concat(Enumerable.Repeat("\"unrelated\" { ", SteamInputLimits.MaxVdfDepth + 1)) +
            string.Concat(Enumerable.Repeat(" }", SteamInputLimits.MaxVdfDepth + 1));

        FormatException error = Should.Throw<FormatException>(() => VdfParser.SelectValue(
            nested,
            ["UserLocalConfigStore", "WebStorage"],
            "PrivateApps_10",
            "PrivateApps_"));

        error.Message.ShouldContain("depth limit");
    }
}
