using BillingRecon.Infrastructure.Common.Time;

namespace BillingRecon.IntegrationTests.Time;

/// <summary>The real clock over a fixed time source: UTC instants, business dates in India, and the fail-fast zone lookup (ADR 0010).</summary>
public sealed class SystemClockTests
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.CreateCustomTimeZone("India (test)", new TimeSpan(5, 30, 0), "India (test)", "India (test)");

    [Fact]
    public void The_instant_comes_from_the_time_provider_and_is_utc()
    {
        var now = new DateTimeOffset(2026, 10, 3, 6, 30, 15, TimeSpan.Zero);

        var clock = Clock(now);

        clock.UtcNow.ShouldBe(now);
        clock.UtcNow.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void The_instant_has_whole_milliseconds_like_the_column_that_stores_it()
    {
        var millisecond = new DateTimeOffset(2026, 10, 3, 6, 30, 15, 123, TimeSpan.Zero);

        Clock(millisecond.AddTicks(9_999)).UtcNow.ShouldBe(millisecond);
        Clock(millisecond).UtcNow.ShouldBe(millisecond);
    }

    [Fact]
    public void A_time_source_that_answers_with_an_offset_is_still_read_as_utc()
    {
        var inIndia = new DateTimeOffset(2026, 10, 3, 12, 0, 15, TimeSpan.FromMinutes(330));

        var now = Clock(inIndia).UtcNow;

        now.ShouldBe(new DateTimeOffset(2026, 10, 3, 6, 30, 15, TimeSpan.Zero));
        now.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-03-31T18:29:59Z", "2026-03-31")] // 23:59:59 in India: still the last day of fiscal year 2025-26
    [InlineData("2026-03-31T18:30:00Z", "2026-04-01")] // midnight in India: the first day of fiscal year 2026-27
    [InlineData("2026-10-03T00:00:00Z", "2026-10-03")]
    [InlineData("2026-12-31T18:30:00Z", "2027-01-01")]
    [InlineData("2028-02-28T18:30:00Z", "2028-02-29")] // leap day
    public void Today_is_the_calendar_date_in_india(string utcNow, string expected)
    {
        var clock = Clock(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));

        clock.TodayIst.ShouldBe(DateOnly.ParseExact(expected, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_instant_is_converted_whatever_offset_it_is_written_with()
    {
        var clock = Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var lateEveningInNewYork = new DateTimeOffset(2026, 3, 31, 14, 30, 0, TimeSpan.FromHours(-4)); // 18:30 UTC

        clock.ToIstDate(lateEveningInNewYork).ShouldBe(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void The_clock_of_the_running_system_knows_indias_time_zone()
    {
        // The parameterless constructor is the one the API uses. On a machine without time zone data it throws.
        var clock = new SystemClock();

        clock.ToIstDate(new DateTimeOffset(2026, 3, 31, 18, 29, 59, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 3, 31));
        clock.ToIstDate(new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 4, 1));
        clock.UtcNow.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void The_iana_id_is_looked_up_first()
    {
        var asked = new List<string>();

        _ = new SystemClock(At(default), id =>
        {
            asked.Add(id);
            return India;
        });

        asked.ShouldBe(["Asia/Kolkata"]);
    }

    [Theory]
    [InlineData(typeof(TimeZoneNotFoundException))]
    [InlineData(typeof(InvalidTimeZoneException))]
    public void The_windows_id_is_used_when_the_iana_id_is_not_known(Type failure)
    {
        var clock = new SystemClock(
            At(new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero)),
            id => id == "India Standard Time" ? India : throw (Exception)Activator.CreateInstance(failure)!);

        clock.TodayIst.ShouldBe(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void A_machine_without_indias_time_zone_stops_the_clock_from_being_created()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new SystemClock(At(default), _ => throw new TimeZoneNotFoundException()));

        exception.Message.ShouldContain("Asia/Kolkata");
        exception.Message.ShouldContain("India Standard Time");
        exception.Message.ShouldContain("tzdata");
    }

    [Fact]
    public void Any_other_failure_of_the_lookup_is_not_swallowed()
    {
        Should.Throw<UnauthorizedAccessException>(
            () => new SystemClock(At(default), _ => throw new UnauthorizedAccessException()));
    }

    private static SystemClock Clock(DateTimeOffset utcNow) => new(At(utcNow), _ => India);

    private static FixedTimeProvider At(DateTimeOffset utcNow) => new(utcNow);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
