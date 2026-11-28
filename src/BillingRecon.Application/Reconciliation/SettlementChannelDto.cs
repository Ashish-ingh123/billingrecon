using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// A settlement channel as the API returns it (BR-1001). <see cref="Version"/> is the row version in base64, the same
/// value as the response's ETag; a change sends it back in If-Match (BR-804).
/// </summary>
public sealed record SettlementChannelDto(
    Guid Id,
    SettlementChannelKind Kind,
    string Name,
    string? AccountLabel,
    PaymentGateway? Gateway,
    Guid? BankChannelId,
    int DateWindowDays,
    ColumnMappingDto? Mapping,
    SettlementChannelStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
