using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// The one document a refund takes its money from (BR-307): a payment or a credit note, never both. Made through
/// <see cref="FromPayment"/> or <see cref="FromCreditNote"/>, so that the kind and the id always agree.
/// </summary>
public readonly record struct RefundSource
{
    private RefundSource(RefundSourceKind kind, PaymentId? paymentId, CreditNoteId? creditNoteId)
    {
        Kind = kind;
        PaymentId = paymentId;
        CreditNoteId = creditNoteId;
    }

    /// <summary>Which kind of document it is.</summary>
    public RefundSourceKind Kind { get; }

    /// <summary>The payment, when the kind is <see cref="RefundSourceKind.Payment"/>; otherwise null.</summary>
    public PaymentId? PaymentId { get; }

    /// <summary>The credit note, when the kind is <see cref="RefundSourceKind.CreditNote"/>; otherwise null.</summary>
    public CreditNoteId? CreditNoteId { get; }

    /// <summary>The id of the document, whichever kind it is.</summary>
    public Guid Id => PaymentId?.Value ?? CreditNoteId?.Value ?? Guid.Empty;

    /// <summary>The unapplied cash of payment <paramref name="paymentId"/>.</summary>
    /// <exception cref="ArgumentException">The id is empty.</exception>
    public static RefundSource FromPayment(PaymentId paymentId)
        => paymentId.Value == Guid.Empty
            ? throw new ArgumentException("A refund comes from a payment; the empty id is not one.", nameof(paymentId))
            : new RefundSource(RefundSourceKind.Payment, paymentId, null);

    /// <summary>The available credit of credit note <paramref name="creditNoteId"/>.</summary>
    /// <exception cref="ArgumentException">The id is empty.</exception>
    public static RefundSource FromCreditNote(CreditNoteId creditNoteId)
        => creditNoteId.Value == Guid.Empty
            ? throw new ArgumentException("A refund comes from a credit note; the empty id is not one.", nameof(creditNoteId))
            : new RefundSource(RefundSourceKind.CreditNote, null, creditNoteId);
}
