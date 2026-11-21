using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>A refund as the API answers it (docs/api.md, section 8.10). Exactly one of the two source ids is set.</summary>
public sealed record RefundDto(
    Guid Id,
    Guid PayerId,
    RefundSourceKind SourceKind,
    Guid? PaymentId,
    Guid? CreditNoteId,
    DateOnly RefundDate,
    decimal Amount,
    PaymentMethod Method,
    string? Reference,
    string Reason,
    RefundStatus Status,
    string? Number,
    string? FiscalYear,
    DateTimeOffset? PostedAtUtc,
    DateTimeOffset? VoidedAtUtc,
    string? VoidReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
