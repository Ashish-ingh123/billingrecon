using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// One line of a credit note in a request body (BR-211). With a <c>billLineId</c>, the description, the unit price and
/// the tax rate may be left out and are then taken from that line of the bill; without one, all three are required.
/// A member the API does not know is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreditNoteLineRequest
{
    /// <summary>The line of the bill this line credits (the <c>id</c> of the line in the bill); left out for a line that credits the bill as a whole.</summary>
    public Guid? BillLineId { get; init; }

    /// <summary>What is credited; when left out, the description of the bill line.</summary>
    public string? Description { get; init; }

    /// <summary>How many units: more than 0, at most three decimals.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>The price of one unit: 0 or more, at most four decimals; when left out, the unit price of the bill line.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>The discount on the line: 0 or more, at most two decimals, not more than the line amount; 0 when left out.</summary>
    public decimal? DiscountAmount { get; init; }

    /// <summary>The tax rate in percent: 0 to 100, at most two decimals; when left out, the tax rate of the bill line.</summary>
    public decimal? TaxRatePercent { get; init; }

    /// <summary>The line as the use case takes it.</summary>
    public CreditNoteLineInput ToInput() => new()
    {
        BillLineId = BillLineId is { } billLineId ? new BillLineId(billLineId) : null,
        Description = Description,
        Quantity = Quantity,
        UnitPrice = UnitPrice,
        DiscountAmount = DiscountAmount,
        TaxRatePercent = TaxRatePercent,
    };
}
