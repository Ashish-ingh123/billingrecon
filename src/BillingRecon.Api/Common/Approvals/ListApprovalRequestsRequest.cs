using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Common.Approvals;

/// <summary>The query string of <c>GET /api/v1/approval-requests</c>: <c>status</c>, <c>documentType</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListApprovalRequestsRequest
{
    /// <summary>Pending, Approved or Rejected; Pending when left out.</summary>
    public ApprovalStatus? Status { get; init; }

    /// <summary>Only requests about this kind of document; every kind when left out.</summary>
    public ApprovalDocumentType? DocumentType { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListApprovalRequestsQuery ToQuery() => new()
    {
        Status = Status ?? ApprovalStatus.Pending,
        DocumentType = DocumentType,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
