using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads settlement channels, untracked and filtered by the tenant.</summary>
public interface ISettlementChannelReadService
{
    /// <summary>The channel, or null when the tenant has none with that id.</summary>
    public Task<SettlementChannelDto?> GetAsync(SettlementChannelId id, CancellationToken ct);

    /// <summary>A page of the tenant's channels, by name.</summary>
    public Task<PageOf<SettlementChannelDto>> ListAsync(ListSettlementChannelsQuery query, CancellationToken ct);

    /// <summary>Whether another channel of the tenant than <paramref name="except"/> has the name, ignoring case.</summary>
    public Task<bool> NameExistsAsync(string name, SettlementChannelId? except, CancellationToken ct);

    /// <summary>Whether <paramref name="id"/> is an active bank channel of the tenant: the one a gateway may settle into.</summary>
    public Task<bool> IsActiveBankAsync(SettlementChannelId id, CancellationToken ct);

    /// <summary>Whether an active gateway channel settles into <paramref name="bankId"/>: its UPI and card money then comes by the gateway's settlement (ADR 0066).</summary>
    public Task<bool> HasGatewayIntoAsync(SettlementChannelId bankId, CancellationToken ct);
}
