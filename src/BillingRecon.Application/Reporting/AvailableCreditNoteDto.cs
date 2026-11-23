namespace BillingRecon.Application.Reporting;

/// <summary>A posted credit note that still holds credit not applied or refunded (BR-213).</summary>
public sealed record AvailableCreditNoteDto(
    Guid CreditNoteId,
    string Number,
    DateOnly CreditNoteDate,
    Guid BillId,
    string BillNumber,
    Guid PayerId,
    string PayerName,
    decimal Total,
    decimal AvailableAmount);
