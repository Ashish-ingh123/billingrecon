using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The query string of <c>GET /api/v1/payers</c>: <c>search</c>, <c>status</c>, <c>patientId</c>, <c>page</c> and
/// <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListPayersRequest
{
    /// <summary>Part of a name.</summary>
    public string? Search { get; init; }

    /// <summary>Active or Inactive; both when left out.</summary>
    public PayerStatus? Status { get; init; }

    /// <summary>Only the payer linked to this patient.</summary>
    public Guid? PatientId { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListPayersQuery ToQuery() => new()
    {
        Search = Search,
        Status = Status,
        PatientId = PatientId is { } patientId ? new PatientId(patientId) : null,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
