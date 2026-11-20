using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// One line of a bill as the client sent it (BR-201). A line that names a service item may leave out the description,
/// the unit price and the tax rate: each one left out is taken from the item. A line without a service item is free
/// text and must carry all three. A discount left out is zero.
/// </summary>
public sealed record BillLineInput
{
    /// <summary>The catalogue entry the line is priced from; null for a free-text line.</summary>
    public ServiceItemId? ServiceItemId { get; init; }

    /// <summary>What is charged; when blank, the name of the service item.</summary>
    public string? Description { get; init; }

    /// <summary>How many units; null when the client sent none.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>The price of one unit; when null, the default price of the service item.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>The discount on the line; zero when null.</summary>
    public decimal? DiscountAmount { get; init; }

    /// <summary>The tax rate in percent; when null, the tax rate of the service item.</summary>
    public decimal? TaxRatePercent { get; init; }
}
