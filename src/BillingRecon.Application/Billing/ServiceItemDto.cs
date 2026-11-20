using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>
/// A service item as the API returns it. <see cref="DefaultUnitPrice"/> has up to four decimals and
/// <see cref="TaxRatePercent"/> up to two. <see cref="Version"/> is the row version in base64, the same value the
/// response carries in its ETag header; a change sends it back in If-Match (BR-804, ADR 0007).
/// </summary>
public sealed record ServiceItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Category,
    decimal DefaultUnitPrice,
    decimal TaxRatePercent,
    ServiceItemStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
