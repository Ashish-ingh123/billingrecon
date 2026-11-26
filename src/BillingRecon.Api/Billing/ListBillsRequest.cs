using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The query string of <c>GET /api/v1/bills</c>: <c>patientId</c>, <c>payerId</c>, <c>status</c>, <c>billDateFrom</c>,
/// <c>billDateTo</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListBillsRequest
{
    /// <summary>Only the bills for this patient.</summary>
    public Guid? PatientId { get; init; }

    /// <summary>Only the bills owed by this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Draft or Voided; both when left out.</summary>
    public BillStatus? Status { get; init; }

    /// <summary>Only the bills dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? BillDateFrom { get; init; }

    /// <summary>Only the bills dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? BillDateTo { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListBillsQuery ToQuery() => new()
    {
        PatientId = PatientId is { } patientId ? new PatientId(patientId) : null,
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Status = Status,
        BillDateFrom = BillDateFrom,
        BillDateTo = BillDateTo,
        Search = Search,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
