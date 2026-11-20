using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Reads the current tenant's settings from its own row, untracked. The tenant filter of the context already limits a
/// tenant to its own row; the id is named as well, so that system work, which sees every row, cannot read a tenant's
/// settings by accident.
/// </summary>
public sealed class TenantSettingsReader(BillingReconDbContext context, ITenantContext tenantContext) : ITenantSettingsReader
{
    /// <inheritdoc />
    public async Task<TenantDocumentSettings> GetDocumentSettingsAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("The settings of a tenant were asked for in a scope that has no tenant.");

        var settings = await context.Set<Tenant>()
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new TenantDocumentSettings(tenant.MaxBackdateDays, tenant.DefaultDueDays, tenant.PostingApprovalThreshold))
            .SingleOrDefaultAsync(ct);

        return settings ?? throw new InvalidOperationException($"Tenant {tenantId} has no row, so it has no settings.");
    }
}
