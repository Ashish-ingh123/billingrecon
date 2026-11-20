namespace BillingRecon.Domain.Common;

/// <summary>A checker rejected a request, with a comment (BR-605): the document is as it was before the maker asked.</summary>
public sealed record ApprovalRequestRejected(ApprovalRequestId ApprovalRequestId, ApprovalDocumentType DocumentType, Guid DocumentId, UserId CheckerUserId, string Reason) : IDomainEvent, IHasReason;
