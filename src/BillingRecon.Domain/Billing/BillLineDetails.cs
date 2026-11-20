using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// What a clerk enters for one line of a bill (BR-201), with every default already filled in by the use case: a line
/// priced from the service catalogue arrives here with the description, price and tax rate it took from the item, or
/// the ones the clerk put in their place. The quantity is never the default value of its type, which is the invalid
/// zero.
/// </summary>
public sealed record BillLineDetails
{
    /// <summary>The catalogue entry the line was priced from; null for a free-text line.</summary>
    public ServiceItemId? ServiceItemId { get; init; }

    /// <summary>What is charged, as the bill shows it.</summary>
    public required string Description { get; init; }

    /// <summary>How many units, more than zero.</summary>
    public required Quantity Quantity { get; init; }

    /// <summary>The price of one unit.</summary>
    public required UnitPrice UnitPrice { get; init; }

    /// <summary>The discount on the line: zero or more, and not more than the line amount.</summary>
    public Money DiscountAmount { get; init; }

    /// <summary>The tax rate of the line.</summary>
    public required Percent TaxRate { get; init; }
}
