using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>
/// What one posting does to the three cached balances of its payer (BR-404; docs/database.md, section 5.3): each
/// amount is positive when the balance grows and negative when it falls.
/// </summary>
/// <param name="Receivable">The change of what the payer owes on bills.</param>
/// <param name="UnappliedCash">The change of the money held for the payer and not yet applied.</param>
/// <param name="CreditAvailable">The change of the credit granted to the payer and not yet used.</param>
public readonly record struct PayerBalanceChange(Money Receivable, Money UnappliedCash, Money CreditAvailable)
{
    /// <summary>True when the posting moves none of the three balances.</summary>
    public bool IsNone => Receivable.IsZero && UnappliedCash.IsZero && CreditAvailable.IsZero;
}
