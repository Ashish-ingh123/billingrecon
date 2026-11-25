using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A bill as the API returns it, with its lines in the order of their numbers. The totals are the sums of the lines
/// (BR-203). <see cref="Version"/> is the row version in base64, the same value the response carries in its ETag
/// header; a change sends it back in If-Match (BR-804, ADR 0007). It changes whenever the bill or any of its lines does.
/// </summary>
public sealed record BillDto(
    Guid Id,
    Guid PatientId,
    Guid PayerId,
    DateOnly BillDate,
    DateOnly DueDate,
    string? EncounterReference,
    string? Notes,
    DocumentOrigin Origin,
    string? LegacyNumber,
    BillStatus Status,
    string? Number,
    string? FiscalYear,
    DateTimeOffset? PostedAtUtc,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal BalanceDue,
    DateTimeOffset? VoidedAtUtc,
    string? VoidReason,
    IReadOnlyList<BillLineDto> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
