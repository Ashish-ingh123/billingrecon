using BillingRecon.Application.Audit;
using BillingRecon.Domain.Audit;
using BillingRecon.Infrastructure.Common.Persistence;

namespace BillingRecon.Infrastructure.Audit;

/// <summary>
/// Adds audit records to the context of the request, so the save of the unit of work inserts them in the transaction
/// of the change they describe (BR-701). The tenant guard gives each record the current tenant.
/// </summary>
public sealed class AuditWriter(BillingReconDbContext context) : IAuditWriter
{
    /// <inheritdoc />
    public void Append(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        context.Set<AuditRecord>().Add(record);
    }
}
