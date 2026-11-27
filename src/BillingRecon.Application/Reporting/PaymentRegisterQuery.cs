using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the payment register (BR-901): the payments of a period by payment date, the oldest first, reversed ones
/// included, with the totals of the whole period. The filters combine.
/// </summary>
public sealed record PaymentRegisterQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "paymentDate", "amount", "unappliedAmount"];

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

    /// <summary>Only the payments of this amount or more.</summary>
    public decimal? AmountFrom { get; init; }

    /// <summary>Only the payments of this amount or less.</summary>
    public decimal? AmountTo { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Sort { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
