using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Lists the tenant's service catalogue one page at a time, by code. <see cref="Search"/> matches part of a code or of
/// a name; <see cref="Status"/> keeps the active or the inactive items only.
/// </summary>
public sealed record ListServiceItemsQuery : IPagedQuery
{
    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>Part of a code or of a name.</summary>
    public string? Search { get; init; }

    /// <summary>Active or Inactive; both when null.</summary>
    public ServiceItemStatus? Status { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
