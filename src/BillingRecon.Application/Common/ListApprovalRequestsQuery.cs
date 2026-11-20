using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// One page of the tenant's approval requests, oldest first: the checker's inbox when <see cref="Status"/> is
/// Pending, which it is unless the client says otherwise (BR-605).
/// </summary>
public sealed record ListApprovalRequestsQuery : IPagedQuery
{
    /// <summary>Only requests in this state; Pending when the client names none.</summary>
    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;

    /// <summary>Only requests about this kind of document; null lists every kind.</summary>
    public ApprovalDocumentType? DocumentType { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
