using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Sets up a settlement channel (BR-1001): a bank with its column mapping, or a gateway with the bank channel it settles
/// into. The kind and the gateway are fixed from then on.
/// </summary>
public sealed record CreateSettlementChannelCommand : ISettlementChannelInput
{
    /// <summary>Bank or Gateway.</summary>
    public SettlementChannelKind? Kind { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string? AccountLabel { get; init; }

    /// <inheritdoc />
    public int? DateWindowDays { get; init; }

    /// <summary>The gateway of a gateway channel.</summary>
    public PaymentGateway? Gateway { get; init; }

    /// <inheritdoc />
    public Guid? BankChannelId { get; init; }

    /// <inheritdoc />
    public ColumnMappingDto? Mapping { get; init; }
}
