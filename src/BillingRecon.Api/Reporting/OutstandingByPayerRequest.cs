using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;

namespace BillingRecon.Api.Reporting;

/// <summary>The query string of <c>GET /api/v1/reports/outstanding-by-payer</c>: <c>search</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record OutstandingByPayerRequest
{
    /// <summary>Only payers whose name contains this text.</summary>
    public string? Search { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public OutstandingByPayerQuery ToQuery() => new()
    {
        Search = Search,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
