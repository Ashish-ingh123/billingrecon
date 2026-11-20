using BillingRecon.Application.Common;

namespace BillingRecon.Application.Audit;

/// <summary>
/// One page of the audit trail of one record of the tenant (US-43): every change of the entity
/// <see cref="EntityType"/> with the id <see cref="EntityId"/>, oldest first.
/// </summary>
public sealed record ListAuditRecordsQuery : IPagedQuery
{
    /// <summary>The kind of record, such as <c>Patient</c> or <c>Bill</c>.</summary>
    public required string EntityType { get; init; }

    /// <summary>The id of the record.</summary>
    public required Guid EntityId { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
