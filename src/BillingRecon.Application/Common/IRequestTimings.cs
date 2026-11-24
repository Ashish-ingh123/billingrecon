namespace BillingRecon.Application.Common;

/// <summary>
/// The durations measured while the current request ran (ADR 0046). The API writes them into the <c>Server-Timing</c>
/// header of the answer when <c>Diagnostics:ServerTiming</c> is on, which it is for a load test only; otherwise they are
/// measured and nobody reads them.
/// </summary>
public interface IRequestTimings
{
    /// <summary>What was measured so far, in the order it was measured; empty when nothing was.</summary>
    public IReadOnlyList<RequestTiming> Entries { get; }
}
