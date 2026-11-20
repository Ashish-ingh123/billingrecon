namespace BillingRecon.Domain.Common;

/// <summary>
/// The business dates a document may carry today (BR-205): not in the future, and not earlier than today in India
/// minus the number of days the tenant allows. The use case builds it from the clock and the tenant's settings; the
/// aggregate never reads either itself.
/// </summary>
public readonly record struct BusinessDateWindow
{
    /// <summary>The window that ends at <paramref name="today"/> and reaches <paramref name="maxBackdateDays"/> days back.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBackdateDays"/> is negative.</exception>
    public BusinessDateWindow(DateOnly today, int maxBackdateDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBackdateDays);

        Latest = today;
        Earliest = today.AddDays(-maxBackdateDays);
    }

    /// <summary>The earliest date allowed.</summary>
    public DateOnly Earliest { get; }

    /// <summary>The latest date allowed: today in India.</summary>
    public DateOnly Latest { get; }
}
