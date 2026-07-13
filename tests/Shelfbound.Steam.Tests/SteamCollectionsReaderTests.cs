using Shelfbound.Steam.Collections;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class SteamCollectionsReaderTests
{
    // The cloud-storage-namespace-1 value: an array of [entryKey, {value:"<collection-json-string>"}].
    private const string NamespaceJson = """
        [
          ["user-collections.from-tag-Finished", {"value":"{\"id\":\"from-tag-Finished\",\"name\":\"Finished\",\"added\":[10,20],\"removed\":[]}"}],
          ["user-collections.uc-dynamic", {"value":"{\"id\":\"uc-dyn\",\"name\":\"VR Games\",\"added\":[30],\"removed\":[],\"filterSpec\":{\"rules\":1}}"}],
          ["user-collections.from-tag-Hold", {"value":"{\"id\":\"from-tag-Hold\",\"name\":\"Hold\",\"added\":[20]}"}],
          ["sc-version", {"value":"3"}]
        ]
        """;

    [Fact]
    public void Parses_static_collections_into_appid_to_categories()
    {
        var result = SteamCollectionsReader.ParseNamespaceJson(NamespaceJson);

        result.ShouldNotBeNull();
        result[10].ShouldBe(["Finished"]);
        // A game in two collections keeps the order the collections appear in.
        result[20].ShouldBe(["Finished", "Hold"]);
    }

    [Fact]
    public void Skips_dynamic_filterspec_collections()
    {
        var result = SteamCollectionsReader.ParseNamespaceJson(NamespaceJson)!;

        // appId 30 only lived in a dynamic (filterSpec) collection, so it's absent.
        result.ShouldNotContainKey(30);
        result.Values.SelectMany(v => v).ShouldNotContain("VR Games");
    }

    [Fact]
    public void Returns_null_when_no_collections()
    {
        SteamCollectionsReader.ParseNamespaceJson("""[["sc-version",{"value":"3"}]]""").ShouldBeNull();
        SteamCollectionsReader.ParseNamespaceJson("[]").ShouldBeNull();
    }

    [Fact]
    public void Tolerates_a_malformed_collection_without_dropping_the_rest()
    {
        string json = """
            [
              ["user-collections.bad", {"value":"{not valid json"}],
              ["user-collections.ok", {"value":"{\"name\":\"Done\",\"added\":[7]}"}]
            ]
            """;
        var result = SteamCollectionsReader.ParseNamespaceJson(json);
        result.ShouldNotBeNull();
        result[7].ShouldBe(["Done"]);
    }

    [Fact]
    public void Rejects_oversized_namespace_before_json_parsing()
    {
        string oversized = new(' ', SteamInputLimits.MaxNamespaceJsonChars + 1);

        InvalidDataException error = Should.Throw<InvalidDataException>(
            () => SteamCollectionsReader.ParseNamespaceJson(oversized));

        error.Message.ShouldContain("character limit");
    }
}
