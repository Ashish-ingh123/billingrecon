namespace BillingRecon.Domain.Receivables;

/// <summary>The financial event a posting records: one per posting template (docs/database.md, section 5.3).</summary>
public enum LedgerPostingKind
{
    /// <summary>A bill was posted: Dr Receivable(bill), Cr Revenue.</summary>
    BillPosted = 0,

    /// <summary>A payment was recorded: Dr Cash, Cr UnappliedCash(payment).</summary>
    PaymentRecorded = 1,

    /// <summary>A payment was allocated to a bill: Dr UnappliedCash(payment), Cr Receivable(bill).</summary>
    PaymentAllocated = 2,

    /// <summary>An allocation was reversed: the mirror of <see cref="PaymentAllocated"/>.</summary>
    AllocationReversed = 3,

    /// <summary>A payment was reversed: Dr UnappliedCash(payment), Cr Cash.</summary>
    PaymentReversed = 4,

    /// <summary>A credit note was posted: Dr Revenue, Cr CreditAvailable(creditNote).</summary>
    CreditNotePosted = 5,

    /// <summary>Credit was applied to a bill: Dr CreditAvailable(creditNote), Cr Receivable(bill).</summary>
    CreditApplied = 6,

    /// <summary>An application of credit was reversed: the mirror of <see cref="CreditApplied"/>.</summary>
    CreditApplicationReversed = 7,

    /// <summary>An adjustment was posted: Dr Adjustments, Cr Receivable(bill).</summary>
    AdjustmentPosted = 8,

    /// <summary>A refund was posted: Dr UnappliedCash(payment) or CreditAvailable(creditNote), Cr Cash.</summary>
    RefundPosted = 9,

    /// <summary>An opening receivable was brought in: Dr Receivable(bill), Cr OpeningBalances (ADR 0053).</summary>
    OpeningReceivablePosted = 10,

    /// <summary>An opening advance was brought in: Dr OpeningBalances, Cr UnappliedCash(payment) (ADR 0053).</summary>
    OpeningAdvancePosted = 11,
}
