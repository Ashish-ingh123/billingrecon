using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>A page of refunds, by payer, status and source; the filters combine.</summary>
public sealed record ListRefundsQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "refundDate", "amount"];

    /// <summary>Only the refunds to this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the refunds in this state.</summary>
    public RefundStatus? Status { get; init; }

    /// <summary>Only the refunds from this payment.</summary>
    public PaymentId? PaymentId { get; init; }

    /// <summary>Only the refunds from this credit note.</summary>
    public CreditNoteId? CreditNoteId { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the refunds dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? RefundDateFrom { get; init; }

    /// <summary>Only the refunds dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? RefundDateTo { get; init; }

    /// <summary>Only the refunds paid by this method: Cash, Card, UPI, BankTransfer or Cheque.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>Only the refunds of this amount or more.</summary>
    public decimal? AmountFrom { get; init; }

    /// <summary>Only the refunds of this amount or less.</summary>
    public decimal? AmountTo { get; init; }

    /// <summary>Only the refunds this user made (the id of a user).</summary>
    public UserId? CreatedBy { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
