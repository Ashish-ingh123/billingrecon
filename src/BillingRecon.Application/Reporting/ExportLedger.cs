
namespace BillingRecon.Application.Reporting;

/// <summary>
/// A ledger of the accountant's books that a line of the export goes to (ADR 0062): Reporting's own word for the
/// twelve names a hospital sets, so that it names no type of another module.
/// </summary>
public enum ExportLedger
{
    /// <summary>What payers owe on bills.</summary>
    Receivable = 0,

    /// <summary>Money received and not yet applied.</summary>
    UnappliedCash = 1,

    /// <summary>Credit of credit notes not yet applied or refunded.</summary>
    CreditAvailable = 2,

    /// <summary>The taxable value billed.</summary>
    Revenue = 3,

    /// <summary>The tax billed.</summary>
    TaxOutput = 4,

    /// <summary>Discounts, write-offs and rounding.</summary>
    Adjustments = 5,

    /// <summary>The other side of the opening balances.</summary>
    OpeningBalances = 6,

    /// <summary>Money in cash.</summary>
    Cash = 7,

    /// <summary>Money by card.</summary>
    Card = 8,

    /// <summary>Money by UPI.</summary>
    Upi = 9,

    /// <summary>Money by bank transfer.</summary>
    BankTransfer = 10,

    /// <summary>Money by cheque.</summary>
    Cheque = 11,
}
