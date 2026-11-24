using System.Diagnostics;
using BillingRecon.Application.Common;

namespace BillingRecon.Infrastructure.Common.Numbering;

/// <summary>
/// How long the current request waited for a number series and then held it (docs/requirements.md section 5, ADR 0046).
/// A series serialises its postings: from the moment a posting has its number until its transaction ends, every other
/// posting of the series waits. That hold must stay under 20 ms.
/// <list type="bullet">
/// <item><c>number-wait</c>: from asking for the number to having it, the locked statement included, so the time spent
/// behind other postings of the series;</item>
/// <item><c>number-hold</c>: from having it to the commit or rollback of the transaction, whoever ends it (the unit of
/// work, or the idempotency store while it holds the transaction).</item>
/// </list>
/// Durations from the monotonic timer of <see cref="Stopwatch"/>, never instants: the clock of the application
/// (<c>IClock</c>) stays the one source of instants (ADR 0010). A use case takes at most one number or holds one series,
/// so a request has at most one of each. One instance per request scope; measuring costs two timer readings.
/// </summary>
public sealed class NumberSeriesTimer : IRequestTimings
{
    /// <summary>The name of the wait in the <c>Server-Timing</c> header.</summary>
    public const string WaitName = "number-wait";

    /// <summary>The name of the hold in the <c>Server-Timing</c> header.</summary>
    public const string HoldName = "number-hold";

    private long? _heldSince;
    private TimeSpan? _wait;
    private TimeSpan? _hold;

    /// <inheritdoc />
    public IReadOnlyList<RequestTiming> Entries
    {
        get
        {
            var entries = new List<RequestTiming>(2);
            if (_wait is { } wait)
            {
                entries.Add(new RequestTiming(WaitName, wait));
            }

            if (_hold is { } hold)
            {
                entries.Add(new RequestTiming(HoldName, hold));
            }

            return entries;
        }
    }

    /// <summary>A reading of the timer, taken before the locked statement is sent.</summary>
    public static long Start() => Stopwatch.GetTimestamp();

    /// <summary>The locked statement has answered: the wait ends, and the series is held from now on.</summary>
    /// <param name="askedAt">The reading of <see cref="Start"/> taken before the statement was sent.</param>
    public void Taken(long askedAt)
    {
        var now = Stopwatch.GetTimestamp();
        _wait = Stopwatch.GetElapsedTime(askedAt, now);
        _heldSince = now;
        _hold = null;
    }

    /// <summary>
    /// The transaction of the request has been committed or rolled back, so the series is free again. Does nothing when
    /// no series is held.
    /// </summary>
    public void TransactionEnded()
    {
        if (_heldSince is { } since)
        {
            _hold = Stopwatch.GetElapsedTime(since);
            _heldSince = null;
        }
    }
}
