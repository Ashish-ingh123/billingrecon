using BillingRecon.Application.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>The opening batches of the tenant, newest first, optionally those in one status.</summary>
public sealed record ListOpeningBatchesQuery : IPagedQuery
{
    /// <summary>Only the batches in this status; all when null.</summary>
    public OpeningBatchStatus? Status { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
