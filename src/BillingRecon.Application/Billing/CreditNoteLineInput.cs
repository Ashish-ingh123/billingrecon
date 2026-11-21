using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// One line of a credit note as the client sent it (BR-211). A line that names a line of the bill, by its id,
/// may leave out the description, the unit price and the tax rate: each one left out is taken from that
/// line. A line without one credits the bill as a whole and must carry all three. A discount left out is zero.
/// </summary>
public sealed record CreditNoteLineInput
{
    /// <summary>The line of the bill this line credits; null for a line that credits the bill as a whole.</summary>
    public BillLineId? BillLineId { get; init; }

    /// <summary>What is credited; when blank, the description of the bill line.</summary>
    public string? Description { get; init; }

    /// <summary>How many units; null when the client sent none.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>The price of one unit; when null, the unit price of the bill line.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>The discount on the line; zero when null.</summary>
    public decimal? DiscountAmount { get; init; }

    /// <summary>The tax rate in percent; when null, the tax rate of the bill line.</summary>
    public decimal? TaxRatePercent { get; init; }
}
