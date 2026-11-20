using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// Loads and adds approval requests for the use cases of the documents they are about (BR-604 to BR-606). The
/// request and its document are saved by one commit of the unit of work.
/// </summary>
public interface IApprovalRequestRepository
{
    /// <summary>The pending request of a document, tracked for a decision; null when the document has none (BR-605: at most one).</summary>
    public Task<ApprovalRequest?> GetPendingAsync(ApprovalDocumentType documentType, Guid documentId, CancellationToken ct);

    /// <summary>Adds a new request to the unit of work.</summary>
    public void Add(ApprovalRequest request);
}
