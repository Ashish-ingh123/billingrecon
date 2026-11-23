using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// The query string of <c>GET /api/v1/reports/unapplied-cash</c> and <c>GET /api/v1/reports/available-credit</c>:
/// <c>payerId</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record PayerDocumentsRequest
{
    /// <summary>Only the documents of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query of the unapplied cash, with the defaults of <see cref="Paging"/>.</summary>
    public UnappliedCashQuery ToUnappliedCashQuery() => new()
    {
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };

    /// <summary>The query of the available credit, with the defaults of <see cref="Paging"/>.</summary>
    public AvailableCreditQuery ToAvailableCreditQuery() => new()
    {
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
