using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The prefix changes of the current tenant, the newest first, by status and document type when given.</summary>
public sealed record ListPrefixChangesQuery : IPagedQuery
{
    /// <summary>Only changes with this status.</summary>
    public PrefixChangeStatus? Status { get; init; }

    /// <summary>Only changes of this document type.</summary>
    public DocumentType? DocumentType { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
