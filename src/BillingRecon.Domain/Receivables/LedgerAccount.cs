namespace BillingRecon.Domain.Receivables;

/// <summary>
/// The six accounts of the receivables subledger (BR-403; docs/database.md, section 5.2). The first three are subject
/// accounts: every entry names the bill, payment or credit note it is about, and its payer. The last three are
/// control accounts, which make every posting balance and carry no subject.
/// </summary>
public enum LedgerAccount
{
    /// <summary>What a payer still owes on a bill. Debit balance; subject: the bill.</summary>
    Receivable = 0,

    /// <summary>Money received and not yet applied to a bill. Credit balance; subject: the payment.</summary>
    UnappliedCash = 1,

    /// <summary>Credit granted and not yet applied or refunded. Credit balance; subject: the credit note.</summary>
    CreditAvailable = 2,

    /// <summary>Net billed: bills less credit notes. Control account.</summary>
    Revenue = 3,

    /// <summary>Net money received: payments less refunds and reversals. Control account.</summary>
    Cash = 4,

    /// <summary>Balances given up without money: discounts, write-offs, rounding. Control account.</summary>
    Adjustments = 5,
}
