using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Api.Payers;

/// <summary>The query string of <c>GET /api/v1/patients</c>: <c>search</c>, <c>status</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListPatientsRequest
{
    /// <summary>A part of an MRN or of a name.</summary>
    public string? Search { get; init; }

    /// <summary>Active or Inactive; both when left out.</summary>
    public PatientStatus? Status { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListPatientsQuery ToQuery() => new()
    {
        Search = Search,
        Status = Status,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
