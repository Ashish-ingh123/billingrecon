namespace BillingRecon.Application.Reporting;

/// <summary>Every payer of the report together: how many, and the sums of their balances, each equal to its ledger account.</summary>
public sealed record OutstandingTotalsDto(int Payers, decimal Receivable, decimal UnappliedCash, decimal CreditAvailable, decimal NetOutstanding);
