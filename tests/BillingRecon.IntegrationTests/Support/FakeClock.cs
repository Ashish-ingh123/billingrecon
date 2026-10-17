using BillingRecon.Application.Common;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// A clock that stands still until a test moves it. Like the real clock it shows whole milliseconds in UTC. Business
/// dates use the fixed offset of India (UTC+05:30), so the fake does not depend on the time zone data of the machine
/// that runs the tests.
/// </summary>
public sealed class FakeClock : IClock
{
    private static readonly TimeSpan IndiaOffset = new(5, 30, 0);

    /// <summary>Creates a clock that shows <paramref name="utcNow"/>.</summary>
    public FakeClock(DateTimeOffset utcNow) => UtcNow = WholeMilliseconds(utcNow);

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>How many times <see cref="UtcNow"/> was moved with <see cref="Set"/> or <see cref="Advance"/>.</summary>
    public int Changes { get; private set; }

    /// <inheritdoc />
    public DateOnly TodayIst => ToIstDate(UtcNow);

    /// <inheritdoc />
    public DateOnly ToIstDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(IndiaOffset).DateTime);

    /// <summary>Moves the clock to <paramref name="utcNow"/>.</summary>
    public void Set(DateTimeOffset utcNow)
    {
        UtcNow = WholeMilliseconds(utcNow);
        Changes++;
    }

    /// <summary>Moves the clock forward (or back, with a negative value) by <paramref name="by"/>.</summary>
    public void Advance(TimeSpan by)
    {
        UtcNow = WholeMilliseconds(UtcNow + by);
        Changes++;
    }

    private static DateTimeOffset WholeMilliseconds(DateTimeOffset instant)
        => new(instant.UtcTicks - (instant.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
}
