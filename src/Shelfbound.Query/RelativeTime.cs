namespace Shelfbound.Query;

/// <summary>
/// Formats a timestamp as a human, relative phrase ("3 days ago", "2 weeks ago"). Models weight this
/// far more than a raw "2026-06-24", so recency fields are surfaced both ways. Shared derivation logic;
/// each surface decides whether/how to show it.
/// </summary>
public static class RelativeTime
{
    public static string? Describe(DateTimeOffset? when, DateTimeOffset? now = null)
    {
        if (when is null)
            return null;

        TimeSpan span = (now ?? DateTimeOffset.UtcNow) - when.Value;
        if (span < TimeSpan.Zero)
            return "just now";

        double days = span.TotalDays;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return Ago((int)span.TotalMinutes, "minute");
        if (days < 1) return Ago((int)span.TotalHours, "hour");
        if (days < 2) return "yesterday";
        if (days < 7) return Ago((int)days, "day");
        if (days < 35) return Ago((int)(days / 7), "week");
        if (days < 365) return Ago((int)(days / 30.44), "month");
        return Ago((int)(days / 365.25), "year");
    }

    private static string Ago(int count, string unit) =>
        $"{Math.Max(count, 1)} {unit}{(count == 1 ? "" : "s")} ago";
}
