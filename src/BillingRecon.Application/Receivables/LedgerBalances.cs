namespace BillingRecon.Application.Receivables;

/// <summary>
/// The balances of the tenant's receivable accounts at a moment of the ledger (docs/database.md section 5.2): what payers
/// owe on bills (Receivable, debits less credits), the money received not applied (UnappliedCash, credits less debits),
/// and the credit granted not used (CreditAvailable, credits less debits).
/// </summary>
public sealed record LedgerBalances(decimal Receivable, decimal UnappliedCash, decimal CreditAvailable);
