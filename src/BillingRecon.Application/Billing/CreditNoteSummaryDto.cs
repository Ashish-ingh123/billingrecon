using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>A credit note as a list shows it: everything but its lines, its applications and its void details.</summary>
public sealed record CreditNoteSummaryDto(
    Guid Id,
    Guid BillId,
    Guid PayerId,
    DateOnly CreditNoteDate,
    string Reason,
    CreditNoteStatus Status,
    string? Number,
    decimal Total,
    decimal AvailableAmount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
