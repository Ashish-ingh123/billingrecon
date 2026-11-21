using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>The Billing module's side of <see cref="ICreditNoteSettlement"/>: it loads the credit note and asks the aggregate.</summary>
public sealed class CreditNoteSettlement(ICreditNoteRepository creditNotes) : ICreditNoteSettlement
{
    /// <inheritdoc />
    public async Task<Result<PostedCreditNote>> FindPostedAsync(CreditNoteId creditNoteId, CancellationToken ct)
    {
        var creditNote = await creditNotes.GetAsync(creditNoteId, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        // Only a posted credit note has a number (CK_CreditNotes_NumberIffPosted).
        return creditNote is { Status: CreditNoteStatus.Posted, Number: { } number }
            ? new PostedCreditNote(creditNote.PayerId, creditNote.CreditNoteDate, number, creditNote.AvailableAmount)
            : BillingErrors.CreditNoteNotPosted();
    }

    /// <inheritdoc />
    public async Task<Result<SettledCreditNote>> PayRefundAsync(CreditNoteId creditNoteId, PayerId payerId, string refundNumber, Money amount, CancellationToken ct)
    {
        var creditNote = await creditNotes.GetAsync(creditNoteId, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        if (creditNote.PayerId != payerId)
        {
            throw new InvalidOperationException($"Credit note {creditNote.Id} belongs to another payer than the refund that pays from it.");
        }

        var paid = creditNote.PayRefund(refundNumber, amount);
        if (paid.IsFailure)
        {
            return paid.Error;
        }

        // PayRefund refused a credit note without its number.
        return new SettledCreditNote(creditNote.Number ?? string.Empty, creditNote.AvailableAmount);
    }
}
