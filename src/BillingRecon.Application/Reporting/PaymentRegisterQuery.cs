using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the payment register (BR-901): the payments of a period by payment date, the oldest first, reversed ones
/// included, with the totals of the whole period. The filters combine.
/// </summary>
public sealed record PaymentRegisterQuery : IPagedQuery
{
    /// <summary>The first payment date, with <see cref="To"/>; neither means the current month in India.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last payment date, with <see cref="From"/>.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Only the payments of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the payments received this way.</summary>
    public ReportPaymentMethod? Method { get; init; }

    /// <summary>Only the payments in this state.</summary>
    public ReportPaymentStatus? Status { get; init; }

    /// <summary>The order of the items: the oldest first unless the newest are asked for (the dashboard's recent payments).</summary>
    public ReportOrder Order { get; init; } = ReportOrder.OldestFirst;

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
