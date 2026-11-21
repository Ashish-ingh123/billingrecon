using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Drafts a refund from a payment or a credit note (US-34). Exactly one of the two ids is given.</summary>
public sealed record CreateRefundDraftCommand
{
    /// <summary>The payment whose unapplied cash is paid back; null when the refund comes from a credit note.</summary>
    public PaymentId? PaymentId { get; init; }

    /// <summary>The credit note whose available credit is paid back; null when the refund comes from a payment.</summary>
    public CreditNoteId? CreditNoteId { get; init; }

    /// <summary>The refund date; today in India when left out.</summary>
    public DateOnly? RefundDate { get; init; }

    /// <summary>How much.</summary>
    public decimal? Amount { get; init; }

    /// <summary>How the money is paid back.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>The reference of the payout, required unless the method is cash (BR-301).</summary>
    public string? Reference { get; init; }

    /// <summary>Why.</summary>
    public required string Reason { get; init; }
}
