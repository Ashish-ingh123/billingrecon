namespace BillingRecon.Domain.Common;

/// <summary>A checker approved a request (BR-606): the action on the document happened in the same transaction.</summary>
public sealed record ApprovalRequestApproved(ApprovalRequestId ApprovalRequestId, ApprovalDocumentType DocumentType, Guid DocumentId, UserId CheckerUserId) : IDomainEvent;
