using Shouldly;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Steam.Tests;

public class AppManifestParserTests
{
    [Fact]
    public void Parses_core_fields_and_install_state()
    {
        const string acf = """
            "AppState"
            {
                "appid"       "228980"
                "name"        "Steamworks Common Redistributables"
                "StateFlags"  "4"
                "installdir"  "Steamworks Shared"
                "SizeOnDisk"  "600340940"
                "LastUpdated" "1752652157"
                "LastPlayed"  "0"
            }
            """;

        var m = AppManifestParser.Parse(acf);

        m.AppId.ShouldBe(228980);
        m.Name.ShouldBe("Steamworks Common Redistributables");
        m.IsFullyInstalled.ShouldBeTrue();
        m.InstallDir.ShouldBe("Steamworks Shared");
        m.SizeOnDisk.ShouldBe(600340940);
        m.LastUpdated.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1752652157));
        m.LastPlayed.ShouldBeNull(); // 0 maps to null
    }

    [Fact]
    public void Not_fully_installed_when_state_flag_bit_missing()
    {
        var m = AppManifestParser.Parse("""
            "AppState" { "appid" "1" "name" "X" "StateFlags" "2" }
            """);

        m.IsFullyInstalled.ShouldBeFalse();
    }

    [Fact]
    public void Omits_install_directory_that_is_not_a_single_relative_folder()
    {
        string[] unsafeValues = ["../secret", @"games\\secret", "/home/deck/game", @"C:\\Games\\Game", "C:relative"];

        foreach (string unsafeValue in unsafeValues)
        {
            AppManifest manifest = AppManifestParser.Parse($$"""
                "AppState" { "appid" "1" "name" "X" "installdir" "{{unsafeValue.Replace("\\", "\\\\")}}" }
                """);

            manifest.InstallDir.ShouldBeNull($"'{unsafeValue}' must not cross the snapshot privacy boundary.");
        }
    }
}
