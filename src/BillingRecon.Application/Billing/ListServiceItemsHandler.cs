using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>Lists the tenant's service catalogue one page at a time, by code.</summary>
public sealed class ListServiceItemsHandler(IValidator<ListServiceItemsQuery> validator, IServiceItemReadService items)
    : IQueryHandler<ListServiceItemsQuery, PageOf<ServiceItemDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<ServiceItemDto>>> HandleAsync(ListServiceItemsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await items.ListAsync(query, ct);
    }
}
