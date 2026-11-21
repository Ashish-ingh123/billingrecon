using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What a business module hands to <see cref="IReceivablesLedger"/>: one financial event, as one of the posting
/// templates of docs/database.md section 5.3. Each event gets a named factory here with the slice that builds its
/// document (the first is the bill posting of Prompt 3.5), so that a module never spells out accounts or sides
/// itself and every document of a kind is posted the same way. The constructor is public for the ledger's own tests
/// and for those factories; the ledger checks every template it is given, whoever built it (BR-401, BR-403, BR-405).
/// </summary>
/// <param name="Kind">The financial event.</param>
/// <param name="DocumentType">The kind of document the posting belongs to.</param>
/// <param name="DocumentId">The id of that document.</param>
/// <param name="PayerId">The payer whose balances move.</param>
/// <param name="BusinessDate">The business date of the document (BR-406).</param>
/// <param name="Description">What happened, for a person reading the ledger.</param>
/// <param name="Lines">The lines: two or more, debits equal to credits.</param>
/// <param name="ReversalOf">The posting this one reverses, for a reversal.</param>
public sealed record PostingTemplate(
    LedgerPostingKind Kind,
    LedgerDocumentType DocumentType,
    Guid DocumentId,
    PayerId PayerId,
    DateOnly BusinessDate,
    string Description,
    IReadOnlyList<LedgerLine> Lines,
    LedgerPostingId? ReversalOf = null)
{
    /// <summary>
    /// A bill of <paramref name="total"/> was posted (BR-206): Dr Receivable(bill), Cr Revenue. The payer owes
    /// <paramref name="total"/> more. A bill that comes to nothing has no posting: a line of zero is not an entry
    /// (BR-401 as amended, ADR 0022).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="total"/> is not above zero.</exception>
    public static PostingTemplate BillPosted(BillId billId, PayerId payerId, Money total, DateOnly billDate, DocumentNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (!total.IsPositive)
        {
            throw new ArgumentException("A bill is posted to the ledger for a total above zero; one that comes to nothing has no posting.", nameof(total));
        }

        return new PostingTemplate(
            LedgerPostingKind.BillPosted,
            LedgerDocumentType.Bill,
            billId.Value,
            payerId,
            billDate,
            $"Bill {number} posted",
            [
                LedgerLine.Dr(LedgerAccount.Receivable, total, new LedgerSubject(LedgerSubjectType.Bill, billId.Value)),
                LedgerLine.Cr(LedgerAccount.Revenue, total),
            ]);
    }

    /// <summary>
    /// A payment of <paramref name="amount"/> was recorded (BR-302): Dr Cash, Cr UnappliedCash(payment). The money is
    /// held for the payer, unapplied, until it is allocated to a bill.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero.</exception>
    public static PostingTemplate PaymentRecorded(PaymentId paymentId, PayerId payerId, Money amount, DateOnly paymentDate, DocumentNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (!amount.IsPositive)
        {
            throw new ArgumentException("A payment is recorded for an amount above zero.", nameof(amount));
        }

        return new PostingTemplate(
            LedgerPostingKind.PaymentRecorded,
            LedgerDocumentType.Payment,
            paymentId.Value,
            payerId,
            paymentDate,
            $"Payment {number} recorded",
            [
                LedgerLine.Dr(LedgerAccount.Cash, amount),
                LedgerLine.Cr(LedgerAccount.UnappliedCash, amount, new LedgerSubject(LedgerSubjectType.Payment, paymentId.Value)),
            ]);
    }

    /// <summary>
    /// <paramref name="amount"/> of a payment was allocated to a bill of the same payer (BR-303): the paired entry
    /// Dr UnappliedCash(payment), Cr Receivable(bill). No control account moves: the payer holds less unapplied cash
    /// and owes less, by the same amount (docs/database.md, section 5.4). The business date is the day of the
    /// allocation.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero.</exception>
    public static PostingTemplate PaymentAllocated(
        PaymentAllocationId allocationId,
        PaymentId paymentId,
        BillId billId,
        PayerId payerId,
        Money amount,
        DateOnly allocationDate,
        string paymentNumber,
        string billNumber)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentException("A payment is allocated for an amount above zero.", nameof(amount));
        }

        return new PostingTemplate(
            LedgerPostingKind.PaymentAllocated,
            LedgerDocumentType.PaymentAllocation,
            allocationId.Value,
            payerId,
            allocationDate,
            $"Payment {paymentNumber} allocated to bill {billNumber}",
            [
                LedgerLine.Dr(LedgerAccount.UnappliedCash, amount, new LedgerSubject(LedgerSubjectType.Payment, paymentId.Value)),
                LedgerLine.Cr(LedgerAccount.Receivable, amount, new LedgerSubject(LedgerSubjectType.Bill, billId.Value)),
            ]);
    }

    /// <summary>
    /// A credit note of <paramref name="total"/> was posted (BR-212): Dr Revenue, Cr CreditAvailable(credit note). The
    /// payer has <paramref name="total"/> more credit, which the next posting applies to the bill as far as it goes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="total"/> is not above zero.</exception>
    public static PostingTemplate CreditNotePosted(CreditNoteId creditNoteId, PayerId payerId, Money total, DateOnly creditNoteDate, DocumentNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (!total.IsPositive)
        {
            throw new ArgumentException("A credit note is posted for a total above zero.", nameof(total));
        }

        return new PostingTemplate(
            LedgerPostingKind.CreditNotePosted,
            LedgerDocumentType.CreditNote,
            creditNoteId.Value,
            payerId,
            creditNoteDate,
            $"Credit note {number} posted",
            [
                LedgerLine.Dr(LedgerAccount.Revenue, total),
                LedgerLine.Cr(LedgerAccount.CreditAvailable, total, new LedgerSubject(LedgerSubjectType.CreditNote, creditNoteId.Value)),
            ]);
    }

    /// <summary>
    /// <paramref name="amount"/> of a credit note was applied to a bill of the same payer (BR-212, BR-213): the paired
    /// entry Dr CreditAvailable(credit note), Cr Receivable(bill). No control account moves: the payer has less
    /// credit and owes less, by the same amount. The business date is the day of the application.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero.</exception>
    public static PostingTemplate CreditApplied(
        CreditNoteAllocationId allocationId,
        CreditNoteId creditNoteId,
        BillId billId,
        PayerId payerId,
        Money amount,
        DateOnly applicationDate,
        string creditNoteNumber,
        string billNumber)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentException("Credit is applied for an amount above zero.", nameof(amount));
        }

        return new PostingTemplate(
            LedgerPostingKind.CreditApplied,
            LedgerDocumentType.CreditNoteAllocation,
            allocationId.Value,
            payerId,
            applicationDate,
            $"Credit note {creditNoteNumber} applied to bill {billNumber}",
            [
                LedgerLine.Dr(LedgerAccount.CreditAvailable, amount, new LedgerSubject(LedgerSubjectType.CreditNote, creditNoteId.Value)),
                LedgerLine.Cr(LedgerAccount.Receivable, amount, new LedgerSubject(LedgerSubjectType.Bill, billId.Value)),
            ]);
    }

    /// <summary>
    /// An adjustment of <paramref name="amount"/> was posted against a bill (BR-502): Dr Adjustments,
    /// Cr Receivable(bill). The payer owes <paramref name="amount"/> less, and no money and no credit moved.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero.</exception>
    public static PostingTemplate AdjustmentPosted(
        AdjustmentId adjustmentId,
        BillId billId,
        PayerId payerId,
        Money amount,
        DateOnly adjustmentDate,
        DocumentNumber number,
        string billNumber)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (!amount.IsPositive)
        {
            throw new ArgumentException("An adjustment is posted for an amount above zero.", nameof(amount));
        }

        return new PostingTemplate(
            LedgerPostingKind.AdjustmentPosted,
            LedgerDocumentType.Adjustment,
            adjustmentId.Value,
            payerId,
            adjustmentDate,
            $"Adjustment {number} posted against bill {billNumber}",
            [
                LedgerLine.Dr(LedgerAccount.Adjustments, amount),
                LedgerLine.Cr(LedgerAccount.Receivable, amount, new LedgerSubject(LedgerSubjectType.Bill, billId.Value)),
            ]);
    }

    /// <summary>
    /// An allocation of <paramref name="amount"/> was reversed (BR-305): the mirror pair Dr Receivable(bill),
    /// Cr UnappliedCash(payment), naming the posting it reverses. The payer owes the amount again and holds it again
    /// as unapplied cash. The business date is the day of the reversal; the original posting is not touched.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero, or <paramref name="reversalOf"/> is empty.</exception>
    public static PostingTemplate AllocationReversed(
        PaymentAllocationId allocationId,
        PaymentId paymentId,
        BillId billId,
        PayerId payerId,
        Money amount,
        DateOnly reversalDate,
        string paymentNumber,
        string billNumber,
        LedgerPostingId reversalOf)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentException("An allocation is reversed for its amount, which is above zero.", nameof(amount));
        }

        if (reversalOf.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal names the posting it reverses.", nameof(reversalOf));
        }

        return new PostingTemplate(
            LedgerPostingKind.AllocationReversed,
            LedgerDocumentType.PaymentAllocation,
            allocationId.Value,
            payerId,
            reversalDate,
            $"Allocation of payment {paymentNumber} to bill {billNumber} reversed",
            [
                LedgerLine.Dr(LedgerAccount.Receivable, amount, new LedgerSubject(LedgerSubjectType.Bill, billId.Value)),
                LedgerLine.Cr(LedgerAccount.UnappliedCash, amount, new LedgerSubject(LedgerSubjectType.Payment, paymentId.Value)),
            ],
            reversalOf);
    }

    /// <summary>
    /// A payment of <paramref name="amount"/> was reversed (BR-306): Dr UnappliedCash(payment), Cr Cash, naming the
    /// posting that recorded it. Its allocations were reversed before, so the whole amount was unapplied; afterwards
    /// the payment holds nothing. The business date is the day of the reversal.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero, or <paramref name="reversalOf"/> is empty.</exception>
    public static PostingTemplate PaymentReversed(PaymentId paymentId, PayerId payerId, Money amount, DateOnly reversalDate, string paymentNumber, LedgerPostingId reversalOf)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentException("A payment is reversed for its amount, which is above zero.", nameof(amount));
        }

        if (reversalOf.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal names the posting it reverses.", nameof(reversalOf));
        }

        return new PostingTemplate(
            LedgerPostingKind.PaymentReversed,
            LedgerDocumentType.Payment,
            paymentId.Value,
            payerId,
            reversalDate,
            $"Payment {paymentNumber} reversed",
            [
                LedgerLine.Dr(LedgerAccount.UnappliedCash, amount, new LedgerSubject(LedgerSubjectType.Payment, paymentId.Value)),
                LedgerLine.Cr(LedgerAccount.Cash, amount),
            ],
            reversalOf);
    }

    /// <summary>
    /// An application of credit of <paramref name="amount"/> was reversed (ADR 0028): the mirror pair
    /// Dr Receivable(bill), Cr CreditAvailable(credit note), naming the posting it reverses. The payer owes the amount
    /// again and has it again as available credit. The business date is the day of the reversal.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero, or <paramref name="reversalOf"/> is empty.</exception>
    public static PostingTemplate CreditApplicationReversed(
        CreditNoteAllocationId applicationId,
        CreditNoteId creditNoteId,
        BillId billId,
        PayerId payerId,
        Money amount,
        DateOnly reversalDate,
        string creditNoteNumber,
        string billNumber,
        LedgerPostingId reversalOf)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentException("An application is reversed for its amount, which is above zero.", nameof(amount));
        }

        if (reversalOf.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal names the posting it reverses.", nameof(reversalOf));
        }

        return new PostingTemplate(
            LedgerPostingKind.CreditApplicationReversed,
            LedgerDocumentType.CreditNoteAllocation,
            applicationId.Value,
            payerId,
            reversalDate,
            $"Application of credit note {creditNoteNumber} to bill {billNumber} reversed",
            [
                LedgerLine.Dr(LedgerAccount.Receivable, amount, new LedgerSubject(LedgerSubjectType.Bill, billId.Value)),
                LedgerLine.Cr(LedgerAccount.CreditAvailable, amount, new LedgerSubject(LedgerSubjectType.CreditNote, creditNoteId.Value)),
            ],
            reversalOf);
    }

    /// <summary>
    /// A refund of <paramref name="amount"/> was posted from what payment <paramref name="paymentId"/> had unapplied
    /// (BR-307): Dr UnappliedCash(payment), Cr Cash. The payer holds less unapplied cash, and the money left. The
    /// business date is the refund date.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero.</exception>
    public static PostingTemplate RefundFromPaymentPosted(
        RefundId refundId,
        PaymentId paymentId,
        PayerId payerId,
        Money amount,
        DateOnly refundDate,
        DocumentNumber number,
        string paymentNumber)
    {
        ArgumentNullException.ThrowIfNull(number);
        RequireRefundAmount(amount);

        return new PostingTemplate(
            LedgerPostingKind.RefundPosted,
            LedgerDocumentType.Refund,
            refundId.Value,
            payerId,
            refundDate,
            $"Refund {number} from payment {paymentNumber}",
            [
                LedgerLine.Dr(LedgerAccount.UnappliedCash, amount, new LedgerSubject(LedgerSubjectType.Payment, paymentId.Value)),
                LedgerLine.Cr(LedgerAccount.Cash, amount),
            ]);
    }

    /// <summary>
    /// A refund of <paramref name="amount"/> was posted from the available credit of credit note
    /// <paramref name="creditNoteId"/> (BR-213, BR-307): Dr CreditAvailable(credit note), Cr Cash. The payer holds less
    /// credit, and the money left. The business date is the refund date.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is not above zero.</exception>
    public static PostingTemplate RefundFromCreditNotePosted(
        RefundId refundId,
        CreditNoteId creditNoteId,
        PayerId payerId,
        Money amount,
        DateOnly refundDate,
        DocumentNumber number,
        string creditNoteNumber)
    {
        ArgumentNullException.ThrowIfNull(number);
        RequireRefundAmount(amount);

        return new PostingTemplate(
            LedgerPostingKind.RefundPosted,
            LedgerDocumentType.Refund,
            refundId.Value,
            payerId,
            refundDate,
            $"Refund {number} from credit note {creditNoteNumber}",
            [
                LedgerLine.Dr(LedgerAccount.CreditAvailable, amount, new LedgerSubject(LedgerSubjectType.CreditNote, creditNoteId.Value)),
                LedgerLine.Cr(LedgerAccount.Cash, amount),
            ]);
    }

    private static void RequireRefundAmount(Money amount)
    {
        if (!amount.IsPositive)
        {
            throw new ArgumentException("A refund is posted for an amount above zero.", nameof(amount));
        }
    }
}
