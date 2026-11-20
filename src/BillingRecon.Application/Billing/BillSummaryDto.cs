using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>A bill as a list shows it: everything but its lines and its void details, which the bill itself returns.</summary>
public sealed record BillSummaryDto(
    Guid Id,
    Guid PatientId,
    Guid PayerId,
    DateOnly BillDate,
    DateOnly DueDate,
    string? EncounterReference,
    BillStatus Status,
    decimal Total,
    decimal BalanceDue,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
