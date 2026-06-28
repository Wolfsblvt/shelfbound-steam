namespace Shelfbound.Core.UserData;

/// <summary>Where a game stands for the user. Centralized so callers don't pass loose strings.</summary>
public enum GameStatus
{
    Unknown = 0,
    WantToPlay,
    Playing,
    Paused,
    Finished,
    Dropped,
    Replayable,
    ComfortGame,
    Ignored,
    PlayedElsewhere,
}

/// <summary>How the user feels about a game.</summary>
public enum GameRating
{
    Unknown = 0,
    Loved,
    Liked,
    Mixed,
    Disliked,
    NeverAgain,
}

/// <summary>What a memory is about.</summary>
public enum MemoryScope
{
    Global = 0,
    Game,
    Category,
}

/// <summary>How sure we are about a stored fact. Model inferences should be Low until the user confirms.</summary>
public enum MemoryConfidence
{
    Low = 0,
    Medium,
    High,
}
