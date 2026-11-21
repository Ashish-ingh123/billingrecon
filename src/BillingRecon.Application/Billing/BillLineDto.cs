namespace BillingRecon.Application.Billing;

/// <summary>
/// One line of a bill as the API returns it (BR-201, BR-202). The id is what a credit note line names when it credits
/// this line (BR-211). The quantity has up to three decimals, the unit price up
/// to four, the tax rate up to two; the three amounts are exact to the paisa.
/// </summary>
public sealed record BillLineDto(
    Guid Id,
    int LineNo,
    Guid? ServiceItemId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineAmount,
    decimal DiscountAmount,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal LineTotal);
