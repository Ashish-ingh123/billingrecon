using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>One page of the posted credit notes that hold available credit (BR-901), the oldest first, with the totals of all of them.</summary>
public sealed record AvailableCreditQuery : IPagedQuery
{
    /// <summary>Only the credit notes of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
