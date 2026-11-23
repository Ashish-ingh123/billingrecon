namespace BillingRecon.Application.Reporting;

/// <summary>The maker-checker requests that wait (BR-901), read without tracking and filtered by the current tenant.</summary>
public interface IApprovalReports
{
    /// <summary>
    /// One page of the pending requests that match <paramref name="query"/>, the oldest first, with the days each has
    /// waited by <paramref name="todayIst"/>, and the totals of all of them.
    /// </summary>
    public Task<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>> PendingApprovalsAsync(PendingApprovalsQuery query, DateOnly todayIst, CancellationToken ct);
}
