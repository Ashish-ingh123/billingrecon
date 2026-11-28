using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Replaces the name, the label, the window and, for a gateway, the bank it settles into, or for a bank its mapping:
/// every field is sent, and a label left out is cleared. The kind and the gateway are not part of it.
/// <see cref="ExpectedVersion"/> is the version the client read (BR-804).
/// </summary>
public sealed record UpdateSettlementChannelCommand : ISettlementChannelInput
{
    /// <summary>The channel to change.</summary>
    public required SettlementChannelId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header.</summary>
    public required string ExpectedVersion { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string? AccountLabel { get; init; }

    /// <inheritdoc />
    public int? DateWindowDays { get; init; }

    /// <inheritdoc />
    public Guid? BankChannelId { get; init; }

    /// <inheritdoc />
    public ColumnMappingDto? Mapping { get; init; }
}
