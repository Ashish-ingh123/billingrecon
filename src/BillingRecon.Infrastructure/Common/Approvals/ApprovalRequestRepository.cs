using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Approvals;

/// <summary>
/// Loads and adds approval requests through the context of the request, so a request and its document are saved by
/// one commit (BR-606). Reads are filtered by the current tenant.
/// </summary>
public sealed class ApprovalRequestRepository(BillingReconDbContext context) : IApprovalRequestRepository
{
    /// <inheritdoc />
    public Task<ApprovalRequest?> GetPendingAsync(ApprovalDocumentType documentType, Guid documentId, CancellationToken ct)
        => context.Set<ApprovalRequest>().SingleOrDefaultAsync(
            request => request.DocumentType == documentType && request.DocumentId == documentId && request.Status == ApprovalStatus.Pending,
            ct);

    /// <inheritdoc />
    public void Add(ApprovalRequest request) => context.Set<ApprovalRequest>().Add(request);
}
