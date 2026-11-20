using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads one service item. Another tenant's item is not found, exactly like an id nobody has (BR-002).</summary>
public sealed class GetServiceItemHandler(IServiceItemReadService items) : IQueryHandler<GetServiceItemQuery, ServiceItemDto>
{
    /// <inheritdoc />
    public async Task<Result<ServiceItemDto>> HandleAsync(GetServiceItemQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = await items.GetAsync(query.Id, ct);

        return item is null ? BillingErrors.ServiceItemNotFound() : item;
    }
}
