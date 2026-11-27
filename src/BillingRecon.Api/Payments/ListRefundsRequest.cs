using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>The query string of <c>GET /api/v1/refunds</c>; every filter is optional and they combine.</summary>
public sealed record ListRefundsRequest
{
    /// <summary>Only the refunds to this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Only the refunds in this state.</summary>
    public RefundStatus? Status { get; init; }

    /// <summary>Only the refunds from this payment.</summary>
    public Guid? PaymentId { get; init; }

    /// <summary>Only the refunds from this credit note.</summary>
    public Guid? CreditNoteId { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the refunds dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public string? RefundDateFrom { get; init; }

    /// <summary>Only the refunds dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public string? RefundDateTo { get; init; }

    /// <summary>Only the refunds paid by this method: Cash, Card, UPI, BankTransfer or Cheque.</summary>
    public string? Method { get; init; }

    /// <summary>Only the refunds of this amount or more.</summary>
    public string? AmountFrom { get; init; }

    /// <summary>Only the refunds of this amount or less.</summary>
    public string? AmountTo { get; init; }

    /// <summary>Only the refunds this user made (the id of a user).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>number</c>, <c>refundDate</c>, <c>amount</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>From 1; 1 when left out.</summary>
    public int? Page { get; init; }

    /// <summary>From 1 to the largest page size; the default page size when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query the request stands for.</summary>
    public Result<ListRefundsQuery> ToQuery()
    {
        var read = new FilterReader();
        var refundDateFrom = read.Date(RefundDateFrom, "refundDateFrom");
        var refundDateTo = read.Date(RefundDateTo, "refundDateTo");
        var method = read.OneOf<PaymentMethod>(Method, "method");
        var amountFrom = read.Amount(AmountFrom, "amountFrom");
        var amountTo = read.Amount(AmountTo, "amountTo");
        var createdBy = read.Id(CreatedBy, "createdBy");
        read.InOrder(refundDateFrom, refundDateTo, "refundDateTo");
        read.InOrder(amountFrom, amountTo, "amountTo");
        var order = read.Sort(Sort, ListRefundsQuery.Sorts);

        return read.Answer(new ListRefundsQuery
        {
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Status = Status,
            PaymentId = PaymentId is { } paymentId ? new PaymentId(paymentId) : null,
            CreditNoteId = CreditNoteId is { } creditNoteId ? new CreditNoteId(creditNoteId) : null,
            Search = Search,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            RefundDateFrom = refundDateFrom,
            RefundDateTo = refundDateTo,
            Method = method,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
            CreatedBy = createdBy is { } createdById ? new UserId(createdById) : null,
            Order = order,
        });
    }
}
