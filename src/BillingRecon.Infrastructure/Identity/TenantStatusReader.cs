using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Reads whether a tenant is active from its own row (ADR 0052), through the request's context, whose tenant filter
/// shows a tenant its own row alone; the answer is kept in <see cref="TenantStatusCache"/>. A tenant without a row is
/// not active.
/// </summary>
public sealed class TenantStatusReader(BillingReconDbContext context, TenantStatusCache cache) : ITenantStatusReader
{
    /// <inheritdoc />
    public async Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct)
    {
        if (cache.Find(tenantId) is { } kept)
        {
            return kept;
        }

        var status = await context.Set<Tenant>()
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => (TenantStatus?)tenant.Status)
            .SingleOrDefaultAsync(ct);
        var active = status == TenantStatus.Active;
        cache.Keep(tenantId, active);

        return active;
    }
}
