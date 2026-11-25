using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>A payment as the API returns it. <c>ReversedAtUtc</c> and <c>ReversalReason</c> are null unless the payment was
/// reversed (BR-306). <c>Version</c> is the row version, sent back as the ETag (BR-804).</summary>
public sealed record PaymentDto(
    Guid Id,
    string Number,
    string FiscalYear,
    Guid PayerId,
    DateOnly PaymentDate,
    PaymentMethod Method,
    string? Reference,
    decimal Amount,
    decimal UnappliedAmount,
    PaymentStatus Status,
    string? Notes,
    DocumentOrigin Origin,
    string? LegacyNumber,
    DateTimeOffset? ReversedAtUtc,
    string? ReversalReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
