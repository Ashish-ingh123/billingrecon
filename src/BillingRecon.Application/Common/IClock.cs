namespace BillingRecon.Application.Common;

/// <summary>
/// The only source of time (BR-003, ADR 0010). Instants are UTC. Business dates are calendar dates in India
/// (Asia/Kolkata, UTC+05:30, no daylight saving). A use case reads <see cref="UtcNow"/> once and uses that value
/// for every stamp it writes.
/// </summary>
public interface IClock
{
    /// <summary>The current instant, always with offset zero.</summary>
    public DateTimeOffset UtcNow { get; }

    /// <summary>Today's calendar date in India.</summary>
    public DateOnly TodayIst { get; }

    /// <summary>The calendar date in India at <paramref name="instant"/>.</summary>
    public DateOnly ToIstDate(DateTimeOffset instant);
}
