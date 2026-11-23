using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the maker-checker requests that wait for a decision (BR-901), the oldest first. The report every role
/// reads; the checkers' inbox (<c>GET /api/v1/approval-requests</c>) stays theirs.
/// </summary>
public sealed record PendingApprovalsQuery : IPagedQuery
{
    /// <summary>Only the requests about this kind of document.</summary>
    public ApprovalDocumentType? DocumentType { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
