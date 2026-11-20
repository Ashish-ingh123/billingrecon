using BillingRecon.Application.Billing;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>Loads service items for a change and adds new ones, through the request's context: filtered by its tenant, saved by its unit of work.</summary>
public sealed class ServiceItemRepository(BillingReconDbContext context) : IServiceItemRepository
{
    /// <inheritdoc />
    public Task<ServiceItem?> GetAsync(ServiceItemId id, CancellationToken ct)
        => context.Set<ServiceItem>().SingleOrDefaultAsync(item => item.Id == id, ct);

    /// <inheritdoc />
    public void Add(ServiceItem item) => context.Set<ServiceItem>().Add(item);
}
