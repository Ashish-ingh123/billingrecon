using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// How the use case of a document finds the approval request it is about to decide (BR-605, BR-804).
/// </summary>
public static class PendingApprovals
{
    /// <summary>
    /// The pending request of a document the caller has just read as waiting, or <c>concurrency_conflict</c> when
    /// there is none. A document that waits always has one, made by the same commit; so when it is gone, another
    /// decision was committed after the caller read the document and before it looked here. The caller read a stale
    /// document, and is told what it is told for any stale version (BR-804): read again. That is why the absence is
    /// an answer and not an exception: two checkers deciding at the same moment is ordinary, not a fault.
    /// </summary>
    public static async Task<Result<ApprovalRequest>> FindPendingAsync(
        this IApprovalRequestRepository approvals,
        ApprovalDocumentType documentType,
        Guid documentId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(approvals);

        var request = await approvals.GetPendingAsync(documentType, documentId, ct);
        if (request is null)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        return request;
    }
}
