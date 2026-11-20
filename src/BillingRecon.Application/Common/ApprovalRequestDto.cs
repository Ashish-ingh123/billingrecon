using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>An approval request as the API returns it: who asked for what on which document, and who decided.</summary>
public sealed record ApprovalRequestDto(
    Guid Id,
    ApprovalDocumentType DocumentType,
    Guid DocumentId,
    ApprovalAction Action,
    ApprovalStatus Status,
    Guid MakerUserId,
    string? MakerComment,
    Guid? CheckerUserId,
    string? CheckerComment,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc);
