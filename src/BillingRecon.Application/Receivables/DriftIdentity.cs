namespace BillingRecon.Application.Receivables;

/// <summary>
/// The identities the reconciliation checks so far (docs/database.md, section 5.5), by the name a
/// <see cref="DriftRow"/> carries. The identities of a document join with the slice that posts that document: the
/// bill's since Prompt 3.5, then those of payments and credit notes.
/// </summary>
public static class DriftIdentity
{
    /// <summary>Identity 1: a posting whose debits differ from its credits, or that has fewer than two entries.</summary>
    public const string UnbalancedPosting = "UnbalancedPosting";

    /// <summary>Identity 2: a posted bill above zero without its posting. A bill that came to nothing has none.</summary>
    public const string BillWithoutPosting = "BillWithoutPosting";

    /// <summary>Identity 2, the other direction: a bill posting whose bill is not posted.</summary>
    public const string PostingWithoutBill = "PostingWithoutBill";

    /// <summary>Identity 3: the balance due of a posted bill differs from its receivable in the ledger.</summary>
    public const string BillBalance = "BillBalance";

    /// <summary>Identity 4: the unapplied amount of a payment differs from its unapplied cash in the ledger.</summary>
    public const string PaymentUnapplied = "PaymentUnapplied";

    /// <summary>Identity 2: a payment without its posting.</summary>
    public const string PaymentWithoutPosting = "PaymentWithoutPosting";

    /// <summary>Identity 2, the other direction: a payment posting without its payment.</summary>
    public const string PostingWithoutPayment = "PostingWithoutPayment";

    /// <summary>Identity 5: the amount of a payment less its active allocations differs from its unapplied amount.</summary>
    public const string PaymentAllocations = "PaymentAllocations";

    /// <summary>Identity 2: an allocation without its posting.</summary>
    public const string AllocationWithoutPosting = "AllocationWithoutPosting";

    /// <summary>Identity 2, the other direction: an allocation posting without its allocation.</summary>
    public const string PostingWithoutAllocation = "PostingWithoutAllocation";

    /// <summary>An allocation whose bill is owed by another payer than its payment came from (BR-303).</summary>
    public const string AllocationPayerMismatch = "AllocationPayerMismatch";

    /// <summary>Identity 2: a reversed allocation without the mirror posting that names its own posting (BR-305).</summary>
    public const string ReversedAllocationNoPosting = "ReversedAllocationNoPosting";

    /// <summary>Identity 2: a reversed payment without the posting that reverses the one that recorded it (BR-306).</summary>
    public const string ReversedPaymentNoPosting = "ReversedPaymentNoPosting";

    /// <summary>Identity 2: a reversed application of credit without the mirror posting that names its own posting (ADR 0028).</summary>
    public const string ReversedApplicationNoPosting = "ReversedApplicationNoPosting";

    /// <summary>Identity 2, the other direction: a reversal posting whose allocation, payment or application of credit is not reversed.</summary>
    public const string ReversalPostingNoDocument = "ReversalPostingNoDocument";

    /// <summary>Identity 5: the available amount of a posted credit note differs from its available credit in the ledger.</summary>
    public const string CreditNoteAvailable = "CreditNoteAvailable";

    /// <summary>Identity 2: a posted credit note without its posting.</summary>
    public const string CreditNoteWithoutPosting = "CreditNoteWithoutPosting";

    /// <summary>Identity 2, the other direction: a credit note posting whose credit note is not posted.</summary>
    public const string PostingWithoutCreditNote = "PostingWithoutCreditNote";

    /// <summary>Identity 5, the second form: the total of a posted credit note less its active applications differs from its available amount.</summary>
    public const string CreditNoteApplications = "CreditNoteApplications";

    /// <summary>Identity 2: an application of credit without its posting.</summary>
    public const string ApplicationWithoutPosting = "ApplicationWithoutPosting";

    /// <summary>Identity 2, the other direction: a posting of applied credit without its application.</summary>
    public const string PostingWithoutApplication = "PostingWithoutApplication";

    /// <summary>An application whose bill is owed by another payer than its credit note belongs to (BR-213).</summary>
    public const string ApplicationPayerMismatch = "ApplicationPayerMismatch";

    /// <summary>The posted credit notes of a bill come to more than the bill (BR-210).</summary>
    public const string CreditNotesExceedBillTotal = "CreditNotesExceedBillTotal";

    /// <summary>Identity 2: a posted adjustment without its posting.</summary>
    public const string AdjustmentWithoutPosting = "AdjustmentWithoutPosting";

    /// <summary>Identity 2, the other direction: an adjustment posting whose adjustment is not posted.</summary>
    public const string PostingWithoutAdjustment = "PostingWithoutAdjustment";

    /// <summary>An adjustment whose payer is not the payer of its bill (BR-501).</summary>
    public const string AdjustmentPayerMismatch = "AdjustmentPayerMismatch";

    /// <summary>Identity 6: the receivable of a payer differs from the ledger.</summary>
    public const string PayerReceivable = "PayerReceivable";

    /// <summary>Identity 7: the unapplied cash of a payer differs from the ledger.</summary>
    public const string PayerUnappliedCash = "PayerUnappliedCash";

    /// <summary>Identity 8: the available credit of a payer differs from the ledger.</summary>
    public const string PayerCreditAvailable = "PayerCreditAvailable";

    /// <summary>A payer with ledger entries and no row of cached balances.</summary>
    public const string PayerWithoutBalances = "PayerWithoutBalances";

    /// <summary>Identity 10: a subject whose balance in the ledger is below zero (BR-405).</summary>
    public const string NegativeSubjectBalance = "NegativeSubjectBalance";
}
