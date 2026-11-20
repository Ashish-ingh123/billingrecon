using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Approvals;

/// <summary>
/// Reads approval requests without tracking them, filtered by the tenant of the request's context, oldest first.
/// The index <c>IX_ApprovalRequests_TenantId_Status_RequestedAtUtc</c> serves the list.
/// </summary>
public sealed class ApprovalRequestReadService(BillingReconDbContext context) : IApprovalRequestReadService
{
    /// <inheritdoc />
    public async Task<PageOf<ApprovalRequestDto>> ListAsync(ListApprovalRequestsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requests = context.Set<ApprovalRequest>().AsNoTracking().Where(request => request.Status == query.Status);

        if (query.DocumentType is { } documentType)
        {
            requests = requests.Where(request => request.DocumentType == documentType);
        }

        var totalCount = await requests.CountAsync(ct);
        var page = await requests
            .OrderBy(request => request.RequestedAtUtc)
            .ThenBy(request => request.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<ApprovalRequestDto>([.. page.Select(request => request.ToDto())], query.Page, query.PageSize, totalCount);
    }
}
