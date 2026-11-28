using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Loads and adds settlement channels for the commands that change them; reads are filtered by the tenant. No remove.</summary>
public interface ISettlementChannelRepository
{
    /// <summary>The channel, tracked for a change, or null when the tenant has none with that id.</summary>
    public Task<SettlementChannel?> GetAsync(SettlementChannelId id, CancellationToken ct);

    /// <summary>Adds a new channel to the unit of work; it is saved by the commit.</summary>
    public void Add(SettlementChannel channel);
}
