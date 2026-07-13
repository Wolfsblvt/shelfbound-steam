using Shelfbound.Steam.Steam;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SharedConfigParserTests
{
    [Fact]
    public void Parses_app_tags_into_ordered_categories()
    {
        const string vdf = """
            "UserRoamingConfigStore"
            {
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "1289310"
                                {
                                    "tags"
                                    {
                                        "0" "Deck"
                                        "1" "Directly Next"
                                    }
                                }
                                "812810" { "tags" { "0" "Finished" } }
                                "1406810" { }
                            }
                        }
                    }
                }
            }
            """;

        var result = SharedConfigParser.Parse(vdf);

        result.Count.ShouldBe(2); // the empty-tags app is skipped
        result[1289310].ShouldBe(["Deck", "Directly Next"]); // numeric tag order preserved
        result[812810].ShouldBe(["Finished"]);
        result.ContainsKey(1406810).ShouldBeFalse();
    }

    [Fact]
    public void Returns_empty_when_structure_is_missing()
    {
        SharedConfigParser.Parse("""
            "UserRoamingConfigStore" { }
            """).ShouldBeEmpty();
    }
}
