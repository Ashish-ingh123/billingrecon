using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>How every refund use case reads the source of a refund (BR-307; ADR 0029).</summary>
internal static class RefundSources
{
    /// <summary>
    /// The facts of <paramref name="source"/>, when money can still be refunded from it: a payment that is posted
    /// (409 <c>payment_pending_reversal</c> or <c>payment_reversed</c> otherwise), or a posted credit note. A source the
    /// tenant does not have is <c>payment_not_found</c> or <c>credit_note_not_found</c>.
    /// </summary>
    public static async Task<Result<RefundSourceFacts>> FindAsync(
        RefundSource source,
        IPaymentRepository payments,
        ICreditNoteSettlement creditNotes,
        CancellationToken ct)
    {
        if (source.PaymentId is { } paymentId)
        {
            var payment = await payments.GetAsync(paymentId, ct);
            if (payment is null)
            {
                return PaymentsErrors.PaymentNotFound();
            }

            var usable = payment.CheckCanRefund();
            if (usable.IsFailure)
            {
                return usable.Error;
            }

            return new RefundSourceFacts(payment.PayerId, payment.PaymentDate, payment.Number, payment.UnappliedAmount, payment);
        }

        var creditNoteId = source.CreditNoteId ?? throw new ArgumentException("A refund source names a payment or a credit note.", nameof(source));
        var creditNote = await creditNotes.FindPostedAsync(creditNoteId, ct);
        if (creditNote.IsFailure)
        {
            return creditNote.Error;
        }

        return new RefundSourceFacts(creditNote.Value.PayerId, creditNote.Value.CreditNoteDate, creditNote.Value.Number, creditNote.Value.AvailableAmount, Payment: null);
    }
}
