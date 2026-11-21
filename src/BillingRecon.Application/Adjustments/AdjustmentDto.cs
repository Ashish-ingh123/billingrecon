using BillingRecon.Domain.Adjustments;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// An adjustment as the API returns it. <see cref="Version"/> is the row version in base64, the value of the ETag; a
/// change sends it back in If-Match (BR-804).
/// </summary>
public sealed record AdjustmentDto(
    Guid Id,
    Guid BillId,
    Guid PayerId,
    DateOnly AdjustmentDate,
    AdjustmentKind Kind,
    decimal Amount,
    string Reason,
    AdjustmentStatus Status,
    string? Number,
    string? FiscalYear,
    DateTimeOffset? PostedAtUtc,
    DateTimeOffset? VoidedAtUtc,
    string? VoidReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
