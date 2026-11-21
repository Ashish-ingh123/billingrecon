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

    /// <summary>From 1; 1 when left out.</summary>
    public int? Page { get; init; }

    /// <summary>From 1 to the largest page size; the default page size when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query the request stands for.</summary>
    public ListRefundsQuery ToQuery() => new()
    {
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Status = Status,
        PaymentId = PaymentId is { } paymentId ? new PaymentId(paymentId) : null,
        CreditNoteId = CreditNoteId is { } creditNoteId ? new CreditNoteId(creditNoteId) : null,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
