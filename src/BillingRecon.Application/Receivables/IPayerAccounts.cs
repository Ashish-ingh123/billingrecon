using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What the Payers module asks of Receivables when a payer is created (BR-404; docs/domain-model.md, section 8): the
/// row of the payer's cached balances, at zero. It is added to the current unit of work, so the payer and its
/// balances are saved by one commit, and every payer has the row the ledger later moves.
/// </summary>
public interface IPayerAccounts
{
    /// <summary>Opens the balances of <paramref name="payerId"/>, a payer the current unit of work is adding.</summary>
    public void Open(PayerId payerId);
}
