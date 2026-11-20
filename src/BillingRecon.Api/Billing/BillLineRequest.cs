using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// One line of a bill in a request body (BR-201). With a <c>serviceItemId</c>, the description, the unit price and the
/// tax rate may be left out and are then taken from the catalogue; without one, all three are required. A member the
/// API does not know is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillLineRequest
{
    /// <summary>The catalogue entry the line is priced from; left out for a free-text line.</summary>
    public Guid? ServiceItemId { get; init; }

    /// <summary>What is charged; when left out, the name of the service item.</summary>
    public string? Description { get; init; }

    /// <summary>How many units: more than 0, at most three decimals.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>The price of one unit: 0 or more, at most four decimals; when left out, the item's default price.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>The discount on the line: 0 or more, at most two decimals, not more than the line amount; 0 when left out.</summary>
    public decimal? DiscountAmount { get; init; }

    /// <summary>The tax rate in percent: 0 to 100, at most two decimals; when left out, the item's tax rate.</summary>
    public decimal? TaxRatePercent { get; init; }

    /// <summary>The line as the use case takes it.</summary>
    public BillLineInput ToInput() => new()
    {
        ServiceItemId = ServiceItemId is { } itemId ? new ServiceItemId(itemId) : null,
        Description = Description,
        Quantity = Quantity,
        UnitPrice = UnitPrice,
        DiscountAmount = DiscountAmount,
        TaxRatePercent = TaxRatePercent,
    };
}
