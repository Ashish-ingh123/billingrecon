namespace BillingRecon.Application.Receivables;

/// <summary>
/// The identities the reconciliation checks so far (docs/database.md, section 5.5), by the name a
/// <see cref="DriftRow"/> carries. The identities of a document (a bill's balance due against its receivable, a
/// posted bill without its posting) join with the slice that posts that document.
/// </summary>
public static class DriftIdentity
{
    /// <summary>Identity 1: a posting whose debits differ from its credits, or that has fewer than two entries.</summary>
    public const string UnbalancedPosting = "UnbalancedPosting";

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
