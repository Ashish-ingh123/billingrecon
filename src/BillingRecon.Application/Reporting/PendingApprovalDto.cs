using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// A maker-checker request that waits for a decision (BR-604, BR-901): what it asks, of which document, by whom and since
/// when, how many days that is in India, and the document's payer and amount where it has them. A prefix change has
/// neither.
/// </summary>
public sealed record PendingApprovalDto(
    Guid ApprovalRequestId,
    ApprovalDocumentType DocumentType,
    ApprovalAction Action,
    Guid DocumentId,
    Guid MakerUserId,
    DateTimeOffset RequestedAtUtc,
    int WaitingDays,
    Guid? PayerId,
    string? PayerName,
    decimal? Amount);
