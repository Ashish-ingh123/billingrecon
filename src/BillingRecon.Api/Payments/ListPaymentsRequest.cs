using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The query string of <c>GET /api/v1/payments</c>: <c>payerId</c>, <c>method</c>, <c>status</c>,
/// <c>paymentDateFrom</c>, <c>paymentDateTo</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListPaymentsRequest
{
    /// <summary>Only payments of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Only payments received this way.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>Only payments in this state.</summary>
    public PaymentStatus? Status { get; init; }

    /// <summary>Only payments dated on or after this day, <c>yyyy-MM-dd</c>.</summary>
    public DateOnly? PaymentDateFrom { get; init; }

    /// <summary>Only payments dated on or before this day, <c>yyyy-MM-dd</c>.</summary>
    public DateOnly? PaymentDateTo { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the payments of this amount or more.</summary>
    public string? AmountFrom { get; init; }

    /// <summary>Only the payments of this amount or less.</summary>
    public string? AmountTo { get; init; }

    /// <summary>Only the payments this user made (the id of a user).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Only the payments of this origin: Standard, or Opening for the advances brought in at the cut-over.</summary>
    public string? Origin { get; init; }

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
    public Result<ListPaymentsQuery> ToQuery()
    {
        var read = new FilterReader();
        var amountFrom = read.Amount(AmountFrom, "amountFrom");
        var amountTo = read.Amount(AmountTo, "amountTo");
        var createdBy = read.Id(CreatedBy, "createdBy");
        var origin = read.OneOf<DocumentOrigin>(Origin, "origin");
        read.InOrder(amountFrom, amountTo, "amountTo");
        var order = read.Sort(Sort, ListPaymentsQuery.Sorts);

        return read.Answer(new ListPaymentsQuery
        {
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Method = Method,
            Status = Status,
            PaymentDateFrom = PaymentDateFrom,
            PaymentDateTo = PaymentDateTo,
            Search = Search,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
            CreatedBy = createdBy is { } createdById ? new UserId(createdById) : null,
            Origin = origin,
            Order = order,
        });
    }
}
