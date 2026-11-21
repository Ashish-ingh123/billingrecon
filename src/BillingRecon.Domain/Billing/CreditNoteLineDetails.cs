using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// What a clerk enters for one line of a credit note (BR-211), with every default already filled in by the use case:
/// a line that names a line of the bill arrives here with the description, price and tax rate it took from that line,
/// or the ones the clerk put in their place. The quantity is never the default value of its type, which is the
/// invalid zero.
/// </summary>
public sealed record CreditNoteLineDetails
{
    /// <summary>The line of the bill this line credits; null for a line that credits the bill as a whole.</summary>
    public BillLineId? BillLineId { get; init; }

    /// <summary>What is credited, as the credit note shows it.</summary>
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
