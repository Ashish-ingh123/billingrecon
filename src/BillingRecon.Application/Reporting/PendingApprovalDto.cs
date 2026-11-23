using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// A maker-checker request that waits for a decision (BR-604, BR-901): what it asks, of which document, by whom, with what
/// comment and since when, how many days that is in India, and the document's payer and amount where it has them. A prefix
/// change has neither. The reversal of an allocation or of an application of credit names the payment or the credit note
/// it belongs to, through which it is decided (ADR 0042 point 2).
/// </summary>
public sealed record PendingApprovalDto(
    Guid ApprovalRequestId,
    ApprovalDocumentType DocumentType,
    ApprovalAction Action,
    Guid DocumentId,
    Guid? ParentDocumentId,
    Guid MakerUserId,
    string? MakerComment,
    DateTimeOffset RequestedAtUtc,
    int WaitingDays,
    Guid? PayerId,
    string? PayerName,
    decimal? Amount);
