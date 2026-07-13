using Shelfbound.Query;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(1, "yesterday")]
    [InlineData(3, "3 days ago")]
    [InlineData(14, "2 weeks ago")]
    [InlineData(70, "2 months ago")]
    [InlineData(800, "2 years ago")]
    public void Describes_days_ago(int daysAgo, string expected) =>
        RelativeTime.Describe(Now.AddDays(-daysAgo), Now).ShouldBe(expected);

    [Fact]
    public void Null_input_describes_to_null() =>
        RelativeTime.Describe(null, Now).ShouldBeNull();

    [Fact]
    public void Future_clamps_to_just_now() =>
        RelativeTime.Describe(Now.AddDays(2), Now).ShouldBe("just now");
}
