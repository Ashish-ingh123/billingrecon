namespace BillingRecon.Domain.Identity;

/// <summary>
/// What the accountant's export names a ledger for (ADR 0062): six of the seven accounts of our ledger, the tax of a
/// bill, which our ledger keeps inside Revenue, and the money received, which the accountant keeps by how it came.
/// The hospital gives each the name of a ledger in its accountant's books; we never decide a chart of accounts.
/// </summary>
public enum AccountingLedger
{
    /// <summary>What payers owe on bills: one control ledger for every patient and insurer.</summary>
    Receivable = 0,

    /// <summary>Money received and not yet applied to a bill (advances).</summary>
    UnappliedCash = 1,

    /// <summary>Credit granted by credit notes and not yet applied or refunded.</summary>
    CreditAvailable = 2,

    /// <summary>The taxable value of bills, less that of credit notes.</summary>
    Revenue = 3,

    /// <summary>The tax of bills, less that of credit notes.</summary>
    TaxOutput = 4,

    /// <summary>Discounts, write-offs and rounding.</summary>
    Adjustments = 5,

    /// <summary>The other side of the opening balances of the cut-over.</summary>
    OpeningBalances = 6,

    /// <summary>Money received in notes and coins.</summary>
    Cash = 7,

    /// <summary>Money received by card.</summary>
    Card = 8,

    /// <summary>Money received by UPI.</summary>
    Upi = 9,

    /// <summary>Money received by bank transfer.</summary>
    BankTransfer = 10,

    /// <summary>Money received by cheque.</summary>
    Cheque = 11,
}
