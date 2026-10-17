using BillingRecon.Application.Common;

namespace BillingRecon.Infrastructure.Common.Time;

/// <summary>
/// The real clock (ADR 0010): UTC instants from the system time provider, business dates in India's time zone.
/// The zone is looked up when the clock is created, which happens once, at startup: a machine or container image
/// without time zone data stops the application there instead of failing on the first business date.
/// The time zone of the machine itself is never read.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>The IANA id of India's time zone; found on Linux, macOS and current Windows.</summary>
    public const string IanaTimeZoneId = "Asia/Kolkata";

    /// <summary>The Windows id of the same zone, tried when the IANA id is not known.</summary>
    public const string WindowsTimeZoneId = "India Standard Time";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _india;

    /// <summary>Creates the clock of the running system.</summary>
    public SystemClock()
#pragma warning disable RS0030 // The one sanctioned read of the system time provider (ADR 0010, ADR 0015). Everything else takes IClock.
        : this(TimeProvider.System, TimeZoneInfo.FindSystemTimeZoneById)
#pragma warning restore RS0030
    {
    }

    /// <summary>Creates a clock over the given time source and time zone lookup. Used by tests.</summary>
    /// <param name="timeProvider">Where the current instant comes from.</param>
    /// <param name="findTimeZoneById">Looks a time zone up by id and throws when the machine does not know it.</param>
    public SystemClock(TimeProvider timeProvider, Func<string, TimeZoneInfo> findTimeZoneById)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(findTimeZoneById);

        _timeProvider = timeProvider;
        _india = FindIndia(findTimeZoneById);
    }

    /// <summary>
    /// The current instant in UTC, in whole milliseconds. That is the precision of the <c>datetimeoffset(3)</c>
    /// columns: an instant with more digits would no longer equal the stored row after a save.
    /// </summary>
    public DateTimeOffset UtcNow
    {
        get
        {
            var ticks = _timeProvider.GetUtcNow().UtcTicks;

            return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public DateOnly TodayIst => ToIstDate(UtcNow);

    /// <inheritdoc />
    public DateOnly ToIstDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _india).DateTime);

    private static TimeZoneInfo FindIndia(Func<string, TimeZoneInfo> findTimeZoneById)
    {
        foreach (var id in (string[])[IanaTimeZoneId, WindowsTimeZoneId])
        {
            try
            {
                return findTimeZoneById(id);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Try the next id; the failure is reported once, below, when no id is known.
            }
        }

        throw new InvalidOperationException(
            $"The time zone of India is not available on this machine (looked for '{IanaTimeZoneId}' and '{WindowsTimeZoneId}'). "
            + "Business dates cannot be computed without it. In a container, use a runtime image that includes time zone data or install the tzdata package.");
    }
}
