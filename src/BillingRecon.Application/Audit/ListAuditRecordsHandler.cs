using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Audit;

/// <summary>Lists the audit trail of one record, one page at a time, oldest first (US-43).</summary>
public sealed class ListAuditRecordsHandler(IValidator<ListAuditRecordsQuery> validator, IAuditReadService audit)
    : IQueryHandler<ListAuditRecordsQuery, PageOf<AuditRecordDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<AuditRecordDto>>> HandleAsync(ListAuditRecordsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await audit.ListAsync(query, ct);
    }
}
