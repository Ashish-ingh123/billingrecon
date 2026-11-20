using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Loads and adds service items for the commands that change them (docs/domain-model.md, section 9). Reads are filtered
/// by the current tenant. There is no remove: an item is deactivated, never deleted.
/// </summary>
public interface IServiceItemRepository
{
    /// <summary>The item, tracked for a change, or null when the tenant has no item with that id.</summary>
    public Task<ServiceItem?> GetAsync(ServiceItemId id, CancellationToken ct);

    /// <summary>Adds a new item to the unit of work; it is saved by the commit.</summary>
    public void Add(ServiceItem item);
}
