namespace BillingRecon.Application.Receivables;

/// <summary>
/// The ledger of the current tenant summed by business date (BR-902, ADR 0037): what the dashboard derives its figures
/// "as of" a day from. Read-only, in the words of the figures rather than of the ledger's accounts, so a module that may
/// not name the Receivables domain can use it.
/// </summary>
public interface ILedgerMovements
{
    /// <summary>The balances of the receivable accounts over every posting dated before <paramref name="date"/>.</summary>
    public Task<LedgerBalances> BalancesBeforeAsync(DateOnly date, CancellationToken ct);

    /// <summary>
    /// The movements of the postings dated <paramref name="from"/> to <paramref name="to"/>, both included, one row per
    /// business date that has any, oldest first.
    /// </summary>
    public Task<IReadOnlyList<LedgerDay>> DaysAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
