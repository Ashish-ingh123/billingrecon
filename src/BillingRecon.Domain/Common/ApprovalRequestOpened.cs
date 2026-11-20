namespace BillingRecon.Domain.Common;

/// <summary>A maker asked for an action on a document that needs a second person (BR-604). No id: the request gets it when it is added to the unit of work.</summary>
public sealed record ApprovalRequestOpened(ApprovalDocumentType DocumentType, Guid DocumentId, ApprovalAction Action, UserId MakerUserId) : IDomainEvent;
