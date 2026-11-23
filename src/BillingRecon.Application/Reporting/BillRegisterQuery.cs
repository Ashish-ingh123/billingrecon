using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the bill register (BR-901): the posted bills of a period by bill date, the oldest first, with the totals of
/// the whole period. The filters combine.
/// </summary>
public sealed record BillRegisterQuery : IPagedQuery
{
    /// <summary>The first bill date, with <see cref="To"/>; neither means the current month in India.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last bill date, with <see cref="From"/>.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Only the bills owed by this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the bills in this state.</summary>
    public ReportBillStatus? Status { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
