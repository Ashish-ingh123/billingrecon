using BillingRecon.Infrastructure.Common.Time;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Time;

/// <summary>The test clock must behave like the real one, or tests built on it prove nothing.</summary>
public sealed class FakeClockTests
{
    [Fact]
    public void It_shows_the_instant_it_was_given_in_utc()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromMinutes(330)));

        clock.UtcNow.ShouldBe(new DateTimeOffset(2026, 10, 3, 6, 30, 0, TimeSpan.Zero));
        clock.UtcNow.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void It_stands_still_until_it_is_moved()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 3, 6, 30, 0, TimeSpan.Zero));

        clock.UtcNow.ShouldBe(clock.UtcNow);

        clock.Advance(TimeSpan.FromMinutes(90));
        clock.UtcNow.ShouldBe(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));

        clock.Set(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        clock.UtcNow.ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        clock.Changes.ShouldBe(2);
    }

    [Fact]
    public void It_shows_whole_milliseconds_like_the_real_clock()
    {
        var millisecond = new DateTimeOffset(2026, 10, 3, 6, 30, 15, 123, TimeSpan.Zero);
        var clock = new FakeClock(millisecond.AddTicks(9_999));

        clock.UtcNow.ShouldBe(millisecond);

        clock.Advance(TimeSpan.FromTicks(15_000));
        clock.UtcNow.ShouldBe(millisecond.AddMilliseconds(1));

        clock.Set(millisecond.AddTicks(1));
        clock.UtcNow.ShouldBe(millisecond);
    }

    [Theory]
    [InlineData("2026-03-31T18:29:59Z", "2026-03-31")]
    [InlineData("2026-03-31T18:30:00Z", "2026-04-01")]
    public void Its_business_date_changes_at_midnight_in_india_like_the_real_clock(string utcNow, string expected)
    {
        var instant = DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture);
        var clock = new FakeClock(instant);

        var today = DateOnly.ParseExact(expected, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        clock.TodayIst.ShouldBe(today);
        clock.ToIstDate(instant).ShouldBe(today);

        // The same answers as the real clock gives for the same instant.
        new SystemClock().ToIstDate(instant).ShouldBe(today);
    }
}
