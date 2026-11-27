using BillingRecon.Api.Common;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The query string of <c>GET /api/v1/service-items</c>: <c>search</c>, <c>status</c>, <c>page</c> and
/// <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListServiceItemsRequest
{
    /// <summary>Part of a code or of a name.</summary>
    public string? Search { get; init; }

    /// <summary>Active or Inactive; both when left out.</summary>
    public ServiceItemStatus? Status { get; init; }

    /// <summary>Only the items of exactly this category.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>code</c>, <c>name</c>, <c>defaultUnitPrice</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public Result<ListServiceItemsQuery> ToQuery()
    {
        var read = new FilterReader();
        var category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
        var order = read.Sort(Sort, ListServiceItemsQuery.Sorts);

        return read.Answer(new ListServiceItemsQuery
        {
            Search = Search,
            Status = Status,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            Category = category,
            Order = order,
        });
    }
}
