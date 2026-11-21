using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Whether a prefix belongs to another kind of document (ADR 0033, point 6), from what the tenant stores: the defaults
/// on its row, the prefix of every series, and every prefix change. A series changes its prefix only through an approved
/// change, so every prefix a series of another type ever had is either its prefix now or the prefix a change came from.
/// A rejected change's new prefix was never used and does not count; a waiting one's does.
/// </summary>
public sealed class PrefixUsage(BillingReconDbContext context, ITenantContext tenantContext) : IPrefixUsage
{
    /// <inheritdoc />
    public async Task<bool> IsTakenAsync(DocumentType documentType, string prefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("A prefix was looked for in a scope that has no tenant.");

        // As stored, not as a tracked tenant may hold it in memory.
        var tenant = await context.Set<Tenant>().AsNoTracking().SingleAsync(candidate => candidate.Id == tenantId, ct);
        if (tenant.Prefixes.Any(other => other.Key != documentType && other.Value == prefix))
        {
            return true;
        }

        if (await context.Set<NumberSeries>().AnyAsync(series => series.DocumentType != documentType && series.Prefix == prefix, ct))
        {
            return true;
        }

        return await context.Set<PrefixChange>().AnyAsync(
            change => change.DocumentType != documentType
                && (change.FromPrefix == prefix || (change.ToPrefix == prefix && change.Status != PrefixChangeStatus.Rejected)),
            ct);
    }
}
