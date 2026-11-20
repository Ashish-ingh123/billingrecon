namespace BillingRecon.Domain.Common;

/// <summary>
/// A domain event that records why something was done: a void, a reversal, a rejection. The audit record of the event
/// carries the reason in a column of its own (BR-701), so that nobody has to read it out of the JSON.
/// </summary>
public interface IHasReason
{
    /// <summary>The reason a person gave.</summary>
    public string Reason { get; }
}
