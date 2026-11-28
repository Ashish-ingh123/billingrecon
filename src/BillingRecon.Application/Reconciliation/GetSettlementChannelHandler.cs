using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads one settlement channel. Another tenant's channel is not found, exactly like an id nobody has (BR-1024).</summary>
public sealed class GetSettlementChannelHandler(ISettlementChannelReadService channels) : IQueryHandler<GetSettlementChannelQuery, SettlementChannelDto>
{
    /// <inheritdoc />
    public async Task<Result<SettlementChannelDto>> HandleAsync(GetSettlementChannelQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var channel = await channels.GetAsync(query.Id, ct);
        return channel is null ? ReconciliationErrors.SettlementChannelNotFound() : channel;
    }
}
