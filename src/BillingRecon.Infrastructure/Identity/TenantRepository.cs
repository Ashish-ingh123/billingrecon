using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The row of the current tenant, tracked, for a change of its settings (ADR 0033).</summary>
public sealed class TenantRepository(BillingReconDbContext context, ITenantContext tenantContext) : ITenantRepository
{
    /// <inheritdoc />
    public async Task<Tenant> GetCurrentAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("The tenant was asked for in a scope that has no tenant.");

        // A tenant sees its own row only (ADR 0015).
        return await context.Set<Tenant>().SingleOrDefaultAsync(tenant => tenant.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} has no row.");
    }
}
