namespace BillingRecon.Application.Billing;

/// <summary>
/// One line of a credit note as the API returns it (BR-202, BR-211). <c>BillLineId</c> is the line of the bill it
/// credits, null when it credits the bill as a whole.
/// </summary>
public sealed record CreditNoteLineDto(
    int LineNo,
    Guid? BillLineId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineAmount,
    decimal DiscountAmount,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal LineTotal);
