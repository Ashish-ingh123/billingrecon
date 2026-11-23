namespace BillingRecon.Application.Receivables;

/// <summary>
/// One business date of the ledger: what bills posted with that date put on Receivable (<see cref="Billed"/>), what
/// payments recorded with that date put on Cash (<see cref="Collected"/>, gross of reversals and refunds), and how much
/// the Receivable and UnappliedCash balances moved that day, every posting counted.
/// </summary>
public sealed record LedgerDay(DateOnly BusinessDate, decimal Billed, decimal Collected, decimal ReceivableChange, decimal UnappliedCashChange);
