using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// What the Billing module answers when another module takes money out of a credit note: today the refunds of the
/// Payments module (BR-213, BR-307; ADR 0029). Built like <see cref="IBillSettlement"/>: the records it answers carry
/// no type of the Billing domain, so the caller needs no module exception (ADR 0003).
/// </summary>
public interface ICreditNoteSettlement
{
    /// <summary>
    /// The payer, the date, the number and the available amount of posted credit note <paramref name="creditNoteId"/>;
    /// <c>credit_note_not_found</c> when the tenant has no such credit note, <c>credit_note_not_posted</c> when it is not
    /// posted.
    /// </summary>
    public Task<Result<PostedCreditNote>> FindPostedAsync(CreditNoteId creditNoteId, CancellationToken ct);

    /// <summary>
    /// Pays back <paramref name="amount"/> of the available credit of <paramref name="creditNoteId"/> for the posted
    /// refund <paramref name="refundNumber"/>: the credit note's available amount falls by it, in the unit of work of
    /// the caller. Refused, and nothing changed, when the credit note is missing, not posted, or has less available.
    /// </summary>
    /// <exception cref="InvalidOperationException">The credit note belongs to another payer than <paramref name="payerId"/>: a refund takes the payer of its source, so this is a bug.</exception>
    public Task<Result<SettledCreditNote>> PayRefundAsync(CreditNoteId creditNoteId, PayerId payerId, string refundNumber, Money amount, CancellationToken ct);
}
