using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the maker-checker requests that wait for a decision (BR-901), the oldest first. The report every role
/// reads; the checkers' inbox (<c>GET /api/v1/approval-requests</c>) stays theirs.
/// </summary>
public sealed record PendingApprovalsQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["requestedAtUtc"];

    /// <summary>Only the requests about this kind of document.</summary>
    public ApprovalDocumentType? DocumentType { get; init; }

    /// <summary>
    /// The kinds of document whose requests the caller is shown, decided by the API from the caller's roles (review
    /// 2026-11, finding R8); null shows every kind. Never read from the request.
    /// </summary>
    public IReadOnlyCollection<ApprovalDocumentType>? VisibleDocumentTypes { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Sort { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
