namespace BillingRecon.Application.Common;

/// <summary>Reads approval requests without tracking them, filtered by the current tenant.</summary>
public interface IApprovalRequestReadService
{
    /// <summary>One page of the requests <paramref name="query"/> asks for, oldest first.</summary>
    public Task<PageOf<ApprovalRequestDto>> ListAsync(ListApprovalRequestsQuery query, CancellationToken ct);
}
