using System.Text.Json.Serialization;

namespace BillingRecon.Api.Payments;

/// <summary>One line of an allocation request: an amount of the payment for one bill. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllocationRequest
{
    /// <summary>A posted bill of the payer the payment came from. Required.</summary>
    public Guid? BillId { get; init; }

    /// <summary>
    /// The amount to apply, above zero, with at most two decimals; not more than the payment has unapplied or the
    /// bill is owed. Required.
    /// </summary>
    public decimal? Amount { get; init; }
}
