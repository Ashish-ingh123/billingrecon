using System.Text.Json.Serialization;

namespace BillingRecon.Api.Billing;

/// <summary>One line of a request to apply credit: an amount of the credit note for one bill. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreditApplicationRequest
{
    /// <summary>A posted bill of the payer the credit note belongs to. Required.</summary>
    public Guid? BillId { get; init; }

    /// <summary>The amount to apply, above zero, with at most two decimals. Required.</summary>
    public decimal? Amount { get; init; }
}
