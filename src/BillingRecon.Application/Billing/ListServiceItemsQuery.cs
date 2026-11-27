using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Lists the tenant's service catalogue one page at a time, by code. <see cref="Search"/> matches part of a code or of
/// a name; <see cref="Status"/> keeps the active or the inactive items only.
/// </summary>
public sealed record ListServiceItemsQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["code", "name", "defaultUnitPrice"];

    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>Part of a code or of a name.</summary>
    public string? Search { get; init; }

    /// <summary>Active or Inactive; both when null.</summary>
    public ServiceItemStatus? Status { get; init; }

    /// <summary>Only the items of exactly this category.</summary>
    public string? Category { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
