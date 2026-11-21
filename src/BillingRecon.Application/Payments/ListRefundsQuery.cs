using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>A page of refunds, by payer, status and source; the filters combine.</summary>
public sealed record ListRefundsQuery : IPagedQuery
{
    /// <summary>Only the refunds to this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the refunds in this state.</summary>
    public RefundStatus? Status { get; init; }

    /// <summary>Only the refunds from this payment.</summary>
    public PaymentId? PaymentId { get; init; }

    /// <summary>Only the refunds from this credit note.</summary>
    public CreditNoteId? CreditNoteId { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
