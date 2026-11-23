namespace BillingRecon.Application.Reporting;

/// <summary>
/// What the dashboard reads besides the ledger (BR-902), without tracking and filtered by the current tenant: the posted
/// bills that still owe something, and the maker-checker requests that may have been waiting in a window.
/// </summary>
public interface IDashboardReports
{
    /// <summary>Every posted bill with a balance due that is not zero: its due date and that balance.</summary>
    public Task<IReadOnlyList<OpenBillDto>> OpenBillsAsync(CancellationToken ct);

    /// <summary>
    /// The requests asked for before <paramref name="before"/> and not decided before <paramref name="notDecidedBefore"/>:
    /// every request that may have been waiting at some moment between the two.
    /// </summary>
    public Task<IReadOnlyList<ApprovalSpanDto>> ApprovalSpansAsync(DateTimeOffset notDecidedBefore, DateTimeOffset before, CancellationToken ct);
}
