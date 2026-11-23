using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>One page of the payments that hold unapplied cash (BR-304, BR-901), the oldest first, with the totals of all of them.</summary>
public sealed record UnappliedCashQuery : IPagedQuery
{
    /// <summary>Only the payments of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
