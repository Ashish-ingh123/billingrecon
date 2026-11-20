using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;

namespace BillingRecon.Api.Audit;

/// <summary>
/// The query string of <c>GET /api/v1/audit-records</c>: <c>entityType</c> and <c>entityId</c>, both required, and
/// the optional <c>page</c> and <c>pageSize</c>.
/// </summary>
public sealed record ListAuditRecordsRequest
{
    /// <summary>The kind of record, such as Patient, Payer, ServiceItem or Bill.</summary>
    public string? EntityType { get; init; }

    /// <summary>The id of the record.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>. A value left out is refused by the validator.</summary>
    public ListAuditRecordsQuery ToQuery() => new()
    {
        EntityType = EntityType ?? string.Empty,
        EntityId = EntityId ?? Guid.Empty,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
