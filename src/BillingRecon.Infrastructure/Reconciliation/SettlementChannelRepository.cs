using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>Loads settlement channels for a change and adds new ones, through the request's context: filtered by its tenant, saved by its unit of work.</summary>
public sealed class SettlementChannelRepository(BillingReconDbContext context) : ISettlementChannelRepository
{
    /// <inheritdoc />
    public Task<SettlementChannel?> GetAsync(SettlementChannelId id, CancellationToken ct)
        => context.Set<SettlementChannel>().SingleOrDefaultAsync(channel => channel.Id == id, ct);

    /// <inheritdoc />
    public void Add(SettlementChannel channel) => context.Set<SettlementChannel>().Add(channel);
}
