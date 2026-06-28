using System.ComponentModel;
using ModelContextProtocol.Server;
using Shelfbound.Core.UserData;
using Shelfbound.Storage;

namespace Shelfbound.Mcp.Tools;

/// <summary>
/// MCP write/read tools for durable, user-owned context (statuses, opinions, completion, memories,
/// category meanings), stored per owner via <see cref="IUserDataStore"/>. Guardrails: only persist what
/// the user explicitly states or asks to remember — never guesses or inferences. See
/// docs/project/mcp-design.md and privacy-and-data.md.
/// </summary>
[McpServerToolType]
public static class UserDataTools
{
    private const string Source = "mcp-conversation";

    [McpServerTool(Name = "record_game_status")]
    [Description("Record where a game stands for the user. Only when the user explicitly says so (e.g. 'I finished it', 'mark this paused').")]
    public static object RecordGameStatus(
        SnapshotContext context, IUserDataStore store,
        [Description("Steam app id.")] int appId,
        [Description("wantToPlay | playing | paused | finished | dropped | replayable | comfortGame | ignored | playedElsewhere")] string status)
    {
        if (!Enum.TryParse(status, ignoreCase: true, out GameStatus parsed) || parsed == GameStatus.Unknown)
            return new { error = $"Invalid status '{status}'." };

        store.Update(context.OwnerId, profile =>
            UserDataActions.UpsertGame(profile, appId, game => game with { Status = parsed }));
        return new { ok = true, appId, status = parsed.ToString() };
    }

    [McpServerTool(Name = "record_game_opinion")]
    [Description("Record the user's opinion of a game: a rating and/or liked/disliked aspects (e.g. 'dark themes'). Only on explicit statements; pass the user's own words as evidence.")]
    public static object RecordGameOpinion(
        SnapshotContext context, IUserDataStore store,
        [Description("Steam app id.")] int appId,
        [Description("loved | liked | mixed | disliked | neverAgain")] string? rating = null,
        [Description("Aspects the user liked, e.g. ['dark themes','meaningful choices'].")] string[]? likedAspects = null,
        [Description("Aspects the user disliked.")] string[]? dislikedAspects = null,
        [Description("The user's own words, stored as evidence.")] string? evidence = null)
    {
        GameRating? parsedRating = null;
        if (rating is not null)
        {
            if (!Enum.TryParse(rating, ignoreCase: true, out GameRating r) || r == GameRating.Unknown)
                return new { error = $"Invalid rating '{rating}'." };
            parsedRating = r;
        }

        store.Update(context.OwnerId, profile =>
        {
            UserDataActions.UpsertGame(profile, appId, game =>
            {
                var liked = likedAspects is null ? game.LikedAspects : game.LikedAspects.Union(likedAspects).ToList();
                var disliked = dislikedAspects is null ? game.DislikedAspects : game.DislikedAspects.Union(dislikedAspects).ToList();
                return game with { Rating = parsedRating ?? game.Rating, LikedAspects = liked, DislikedAspects = disliked };
            });
            if (!string.IsNullOrWhiteSpace(evidence))
                UserDataActions.AddMemory(profile, MemoryScope.Game, appId.ToString(), $"Opinion: {rating}", Source, evidence, MemoryConfidence.High, userConfirmed: true);
            return 0;
        });
        return new { ok = true, appId, rating = parsedRating?.ToString() };
    }

    [McpServerTool(Name = "set_game_completion")]
    [Description("Set how far the user has completed a game (0-100), and optionally whether they played/finished it on another platform.")]
    public static object SetGameCompletion(
        SnapshotContext context, IUserDataStore store,
        [Description("Steam app id.")] int appId,
        [Description("Completion percent 0-100.")] int completionPercent,
        [Description("True if completed/played on another platform.")] bool? playedElsewhere = null)
    {
        int percent = Math.Clamp(completionPercent, 0, 100);
        store.Update(context.OwnerId, profile =>
            UserDataActions.UpsertGame(profile, appId, game =>
                game with { CompletionPercent = percent, PlayedElsewhere = playedElsewhere ?? game.PlayedElsewhere }));
        return new { ok = true, appId, completionPercent = percent };
    }

    [McpServerTool(Name = "remember")]
    [Description("Save a durable fact the user explicitly stated (a preference, a note, a meaning). scope = global (taste), game (give appIdSubject), or category (give categorySubject). Pass the user's own words as evidence. Do NOT save guesses or inferences — only explicit statements or things the user asked you to remember.")]
    public static object Remember(
        SnapshotContext context, IUserDataStore store,
        [Description("The fact to remember, phrased plainly.")] string text,
        [Description("global | game | category")] string scope = "global",
        [Description("App id when scope=game.")] int? appIdSubject = null,
        [Description("Category name when scope=category.")] string? categorySubject = null,
        [Description("The user's own words (evidence).")] string? evidence = null)
    {
        if (!Enum.TryParse(scope, ignoreCase: true, out MemoryScope parsedScope))
            return new { error = $"Invalid scope '{scope}'." };

        string? subject = parsedScope switch
        {
            MemoryScope.Game => appIdSubject?.ToString(),
            MemoryScope.Category => categorySubject,
            _ => null,
        };

        Memory memory = store.Update(context.OwnerId, profile =>
            UserDataActions.AddMemory(profile, parsedScope, subject, text, Source, evidence, MemoryConfidence.High, userConfirmed: true));
        return new { ok = true, id = memory.Id, scope = parsedScope.ToString(), subject };
    }

    [McpServerTool(Name = "set_category_definition")]
    [Description("Record what one of the user's category/collection names means to them (e.g. 'Hold' = 'started but paused, want to return'). Only from explicit user statements.")]
    public static object SetCategoryDefinition(
        SnapshotContext context, IUserDataStore store,
        [Description("Category name exactly as it appears in the library.")] string name,
        [Description("What it means to the user.")] string meaning)
    {
        store.Update(context.OwnerId, profile => UserDataActions.SetCategoryDefinition(profile, name, meaning));
        return new { ok = true, name, meaning };
    }

    [McpServerTool(Name = "get_game_user_data")]
    [Description("Everything Shelfbound remembers about one game: status, rating, completion, aspects, and game-scoped memories.")]
    public static object GetGameUserData(
        SnapshotContext context, IUserDataStore store,
        [Description("Steam app id.")] int appId)
    {
        UserProfile profile = store.Load(context.OwnerId);
        profile.Games.TryGetValue(appId, out GameUserData? data);
        var memories = profile.Memories
            .Where(m => m.Scope == MemoryScope.Game && m.Subject == appId.ToString())
            .ToList();
        return new { appId, data, memories };
    }

    [McpServerTool(Name = "get_remembered")]
    [Description("What Shelfbound remembers about the user: global preferences, category meanings, and memories (optionally filtered by scope). Recall this before recommending or comparing games.")]
    public static object GetRemembered(
        SnapshotContext context, IUserDataStore store,
        [Description("Optional filter: global | game | category.")] string? scope = null)
    {
        UserProfile profile = store.Load(context.OwnerId);
        IEnumerable<Memory> memories = profile.Memories;
        if (scope is not null && Enum.TryParse(scope, ignoreCase: true, out MemoryScope parsed))
            memories = memories.Where(m => m.Scope == parsed);

        return new
        {
            categoryDefinitions = profile.CategoryDefinitions.Values,
            memories = memories.OrderByDescending(m => m.CreatedAt).ToList(),
        };
    }
}
