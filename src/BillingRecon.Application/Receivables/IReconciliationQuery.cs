namespace BillingRecon.Application.Receivables;

/// <summary>
/// Compares every cached balance of the current tenant with the ledger, and the ledger with its own identities
/// (BR-404; docs/database.md, section 5.6). An empty answer means the caches equal the ledger. Read-only: it never
/// corrects what it finds.
/// </summary>
public interface IReconciliationQuery
{
    /// <summary>Every drift row of the current tenant; none when the identities hold.</summary>
    public Task<IReadOnlyList<DriftRow>> FindDriftAsync(CancellationToken ct);
}
