using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Reporting;

/// <summary>The query string of <c>GET /api/v1/reports/pending-approvals</c>: <c>documentType</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record PendingApprovalsRequest
{
    /// <summary>Only the requests about this kind of document.</summary>
    public ApprovalDocumentType? DocumentType { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public PendingApprovalsQuery ToQuery() => new()
    {
        DocumentType = DocumentType,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
