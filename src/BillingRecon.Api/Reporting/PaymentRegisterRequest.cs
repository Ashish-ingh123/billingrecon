using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// The query string of <c>GET /api/v1/reports/payment-register</c>: <c>from</c> and <c>to</c> (both or neither),
/// <c>payerId</c>, <c>method</c>, <c>status</c>, <c>order</c>, <c>page</c> and <c>pageSize</c>.
/// </summary>
public sealed record PaymentRegisterRequest
{
    /// <summary>The first payment date, <c>yyyy-MM-dd</c>, with <see cref="To"/>; neither means the current month in India.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last payment date, <c>yyyy-MM-dd</c>, with <see cref="From"/>.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Only the payments of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Only the payments received this way.</summary>
    public ReportPaymentMethod? Method { get; init; }

    /// <summary>Only the payments in this state.</summary>
    public ReportPaymentStatus? Status { get; init; }

    /// <summary><c>OldestFirst</c> (when left out) or <c>NewestFirst</c>.</summary>
    public ReportOrder? Order { get; init; }

    /// <summary>Only the payments of this amount or more.</summary>
    public string? AmountFrom { get; init; }

    /// <summary>Only the payments of this amount or less.</summary>
    public string? AmountTo { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>number</c>, <c>paymentDate</c>, <c>amount</c>, <c>unappliedAmount</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public Result<PaymentRegisterQuery> ToQuery()
    {
        var read = new FilterReader();
        var amountFrom = read.Amount(AmountFrom, "amountFrom");
        var amountTo = read.Amount(AmountTo, "amountTo");
        read.InOrder(amountFrom, amountTo, "amountTo");
        var order = read.Sort(Sort, PaymentRegisterQuery.Sorts);

        return read.Answer(new PaymentRegisterQuery
        {
            From = From,
            To = To,
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Method = Method,
            Status = Status,
            Order = Order ?? ReportOrder.OldestFirst,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
            Sort = order,
        });
    }
}
