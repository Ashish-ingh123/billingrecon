using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Lists the tenant's settlement channels one page at a time, by name.</summary>
public sealed class ListSettlementChannelsHandler(IValidator<ListSettlementChannelsQuery> validator, ISettlementChannelReadService channels)
    : IQueryHandler<ListSettlementChannelsQuery, PageOf<SettlementChannelDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<SettlementChannelDto>>> HandleAsync(ListSettlementChannelsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await channels.ListAsync(query, ct);
    }
}
