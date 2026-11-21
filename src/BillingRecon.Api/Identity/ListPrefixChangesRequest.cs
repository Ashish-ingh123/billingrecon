using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The query string of <c>GET /api/v1/settings/prefix-changes</c>.</summary>
public sealed record ListPrefixChangesRequest
{
    /// <summary>Only changes with this status: <c>Pending</c>, <c>Approved</c> or <c>Rejected</c>.</summary>
    public PrefixChangeStatus? Status { get; init; }

    /// <summary>Only changes of this document type.</summary>
    public DocumentType? DocumentType { get; init; }

    /// <summary>The page, from 1.</summary>
    public int? Page { get; init; }

    /// <summary>Changes per page.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query.</summary>
    public ListPrefixChangesQuery ToQuery() => new()
    {
        Status = Status,
        DocumentType = DocumentType,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
