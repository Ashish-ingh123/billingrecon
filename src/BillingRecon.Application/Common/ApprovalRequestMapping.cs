using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>Explicit mapping from the approval request to its DTO.</summary>
public static class ApprovalRequestMapping
{
    /// <summary>The request as the API returns it.</summary>
    public static ApprovalRequestDto ToDto(this ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ApprovalRequestDto(
            Id: request.Id.Value,
            DocumentType: request.DocumentType,
            DocumentId: request.DocumentId,
            Action: request.Action,
            Status: request.Status,
            MakerUserId: request.MakerUserId.Value,
            MakerComment: request.MakerComment,
            CheckerUserId: request.CheckerUserId?.Value,
            CheckerComment: request.CheckerComment,
            RequestedAtUtc: request.RequestedAtUtc,
            DecidedAtUtc: request.DecidedAtUtc);
    }
}
