using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A credit note as the API returns it, with its lines in the order of their numbers and the applications of its
/// credit, the oldest first. <see cref="AvailableAmount"/> is the credit not applied yet (BR-213).
/// <see cref="Version"/> is the row version in base64, the value of the ETag; a change sends it back in If-Match
/// (BR-804).
/// </summary>
public sealed record CreditNoteDto(
    Guid Id,
    Guid BillId,
    Guid PayerId,
    Guid PatientId,
    DateOnly CreditNoteDate,
    string Reason,
    CreditNoteStatus Status,
    string? Number,
    string? FiscalYear,
    DateTimeOffset? PostedAtUtc,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal AvailableAmount,
    DateTimeOffset? VoidedAtUtc,
    string? VoidReason,
    IReadOnlyList<CreditNoteLineDto> Lines,
    IReadOnlyList<CreditNoteApplicationDto> Applications,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
