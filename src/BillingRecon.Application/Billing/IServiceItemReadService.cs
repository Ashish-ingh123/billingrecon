using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Reads service items without tracking them: for queries, and for the checks a command makes before it changes
/// anything. Every read is filtered by the current tenant, so another tenant's item is not there at all.
/// </summary>
public interface IServiceItemReadService
{
    /// <summary>The item, or null when the tenant has no item with that id.</summary>
    public Task<ServiceItemDto?> GetAsync(ServiceItemId id, CancellationToken ct);

    /// <summary>One page of the tenant's items that match <paramref name="query"/>, by code, then by id.</summary>
    public Task<PageOf<ServiceItemDto>> ListAsync(ListServiceItemsQuery query, CancellationToken ct);

    /// <summary>
    /// The tenant's items among <paramref name="ids"/>, in no particular order; an id the tenant has no item for is
    /// simply missing from the answer. For the lines of a bill, which take their defaults from the catalogue (BR-201).
    /// </summary>
    public Task<IReadOnlyList<ServiceItemDto>> GetManyAsync(IReadOnlyCollection<ServiceItemId> ids, CancellationToken ct);

    /// <summary>Whether the tenant's catalogue has an item with that code, whatever its status and the case of its letters.</summary>
    public Task<bool> CodeExistsAsync(ServiceItemCode code, CancellationToken ct);
}
