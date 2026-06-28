using Shouldly;
using Shelfbound.Storage;
using Shelfbound.Storage.UserData;

namespace Shelfbound.Steam.Tests;

public sealed class UserDataStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly JsonUserDataStore _store;

    public UserDataStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "shelfbound-userdata-" + Guid.NewGuid().ToString("N"));
        _store = new JsonUserDataStore(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Persists_game_data_memories_and_category_definitions_across_loads()
    {
        const string owner = "76561190000000000";

        _store.Update(owner, profile =>
        {
            UserDataActions.UpsertGame(profile, 292030, g => g with
            {
                Status = GameStatus.Finished,
                Rating = GameRating.Loved,
                CompletionPercent = 100,
            });
            UserDataActions.AddMemory(profile, MemoryScope.Game, "292030", "Loved the writing",
                "test", "I loved the writing", MemoryConfidence.High, userConfirmed: true);
            UserDataActions.SetCategoryDefinition(profile, "Hold", "started but paused");
            return 0;
        });

        // A fresh store instance reads from disk, proving persistence (consistent across CLI + MCP).
        UserProfile reloaded = new JsonUserDataStore(_dir).Load(owner);

        var game = reloaded.Games[292030];
        game.Status.ShouldBe(GameStatus.Finished);
        game.Rating.ShouldBe(GameRating.Loved);
        game.CompletionPercent.ShouldBe(100);
        reloaded.Memories.ShouldHaveSingleItem().Text.ShouldBe("Loved the writing");
        reloaded.CategoryDefinitions["Hold"].Meaning.ShouldBe("started but paused");
    }

    [Fact]
    public void Update_is_a_load_modify_save_transaction()
    {
        const string owner = "local";
        _store.Update(owner, p => UserDataActions.UpsertGame(p, 1, g => g with { Status = GameStatus.Playing }));
        _store.Update(owner, p => UserDataActions.UpsertGame(p, 2, g => g with { Status = GameStatus.Paused }));

        UserProfile profile = _store.Load(owner);
        profile.Games.Count.ShouldBe(2);
        profile.Games[1].Status.ShouldBe(GameStatus.Playing);
        profile.Games[2].Status.ShouldBe(GameStatus.Paused);
    }
}
