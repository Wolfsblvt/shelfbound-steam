using Shelfbound.Steam.Steam;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class LibraryFoldersParserTests
{
    [Fact]
    public void Parses_libraries_app_ids_and_labels()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"  "C:\\Program Files (x86)\\Steam"
                    "label" ""
                    "apps"
                    {
                        "228980" "600340940"
                        "250820" "5796124569"
                    }
                }
                "1"
                {
                    "path"  "D:\\Steam"
                    "label" "Games"
                    "apps"
                    {
                        "105600" "801432759"
                    }
                }
            }
            """;

        var libs = LibraryFoldersParser.Parse(vdf);

        libs.Count.ShouldBe(2);
        libs[0].Index.ShouldBe(0);
        libs[0].Label.ShouldBe("library-0"); // empty label is synthesized
        libs[0].AppIds.ShouldBe([228980, 250820], ignoreOrder: true);
        libs[1].Label.ShouldBe("Games");
        libs[1].AppIds.ShouldHaveSingleItem().ShouldBe(105600);
    }
}
