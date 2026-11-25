using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Api.Receivables;

/// <summary>The query string of the list of opening batches.</summary>
public sealed record ListOpeningBatchesRequest
{
    /// <summary>Only the batches in this status.</summary>
    public OpeningBatchStatus? Status { get; init; }

    /// <summary>The page, from 1.</summary>
    public int? Page { get; init; }

    /// <summary>The page size, at most 100.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query of the list.</summary>
    public ListOpeningBatchesQuery ToQuery() => new()
    {
        Status = Status,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
