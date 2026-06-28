namespace Shelfbound.Storage.UserData;

/// <summary>
/// A durable, user-owned fact saved by the user or the model: a note, an opinion, a preference, or a
/// category meaning. Carries provenance (source/evidence/confidence) so it can be reviewed and trusted.
/// See docs/project/mcp-design.md (memory guardrails) and privacy-and-data.md.
/// </summary>
public sealed record Memory
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required MemoryScope Scope { get; init; }

    /// <summary>App id (for <see cref="MemoryScope.Game"/>) or category name (for Category); null for Global.</summary>
    public string? Subject { get; init; }

    /// <summary>Where it came from, e.g. "mcp-conversation" or "manual".</summary>
    public required string Source { get; init; }

    /// <summary>What the user actually said/did that justifies storing this.</summary>
    public string? Evidence { get; init; }

    public MemoryConfidence Confidence { get; init; } = MemoryConfidence.High;

    /// <summary>True if the user explicitly stated/confirmed it (vs a model inference awaiting confirmation).</summary>
    public bool UserConfirmed { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
