using BillingRecon.Application.Common;

namespace BillingRecon.Application.Audit;

/// <summary>
/// Reads the audit trail. Every read is filtered by the current tenant, so another tenant's records are not there at
/// all, and a record that does not exist simply has an empty trail.
/// </summary>
public interface IAuditReadService
{
    /// <summary>One page of the trail <paramref name="query"/> asks for, oldest first.</summary>
    public Task<PageOf<AuditRecordDto>> ListAsync(ListAuditRecordsQuery query, CancellationToken ct);
}
