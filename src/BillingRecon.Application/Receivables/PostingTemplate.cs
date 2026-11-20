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
}
