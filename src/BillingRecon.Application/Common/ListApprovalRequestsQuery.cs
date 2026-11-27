using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// One page of the tenant's approval requests, oldest first: the checker's inbox when <see cref="Status"/> is
/// Pending, which it is unless the client says otherwise (BR-605).
/// </summary>
public sealed record ListApprovalRequestsQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["requestedAtUtc"];

    /// <summary>Only requests in this state; Pending when the client names none.</summary>
    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;

    /// <summary>Only requests about this kind of document; null lists every kind.</summary>
    public ApprovalDocumentType? DocumentType { get; init; }

    /// <summary>Only the requests this user made (the id of a user).</summary>
    public UserId? RequestedBy { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
