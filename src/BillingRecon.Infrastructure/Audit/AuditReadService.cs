using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Audit;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Audit;

/// <summary>
/// Reads the audit trail of one record without tracking it, filtered by the tenant of the request's context, in the
/// order the records were written. The index <c>IX_AuditRecords_TenantId_EntityType_EntityId_Id</c> serves it.
/// </summary>
public sealed class AuditReadService(BillingReconDbContext context) : IAuditReadService
{
    /// <inheritdoc />
    public async Task<PageOf<AuditRecordDto>> ListAsync(ListAuditRecordsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entityType = query.EntityType.Trim();
        var records = context.Set<AuditRecord>().AsNoTracking()
            .Where(record => record.EntityType == entityType && record.EntityId == query.EntityId);

        var totalCount = await records.CountAsync(ct);
        var page = await records
            .OrderBy(record => record.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<AuditRecordDto>([.. page.Select(record => record.ToDto())], query.Page, query.PageSize, totalCount);
    }
}
