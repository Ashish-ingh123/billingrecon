namespace BillingRecon.Domain.Receivables;

/// <summary>The kind of document a subject account is kept for (BR-403).</summary>
public enum LedgerSubjectType
{
    /// <summary>A bill: the subject of <see cref="LedgerAccount.Receivable"/>.</summary>
    Bill = 0,

    /// <summary>A payment: the subject of <see cref="LedgerAccount.UnappliedCash"/>.</summary>
    Payment = 1,

    /// <summary>A credit note: the subject of <see cref="LedgerAccount.CreditAvailable"/>.</summary>
    CreditNote = 2,
}
