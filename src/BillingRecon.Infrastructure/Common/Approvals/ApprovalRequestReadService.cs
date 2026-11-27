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

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.RequestedBy is { } requestedBy)
        {
            requests = requests.Where(request => request.MakerUserId == requestedBy);
        }

        var totalCount = await requests.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<ApprovalRequest> ordered = query.Order switch
        {
            { Column: "requestedAtUtc", Descending: var down } => requests.By(request => request.RequestedAtUtc, down).Then(request => request.Id, down),
            null => requests
                .OrderBy(request => request.RequestedAtUtc)
                .ThenBy(request => request.Id)
,
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<ApprovalRequestDto>([.. page.Select(request => request.ToDto())], query.Page, query.PageSize, totalCount);
    }
}
