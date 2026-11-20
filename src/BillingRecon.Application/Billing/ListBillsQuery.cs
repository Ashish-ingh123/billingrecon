using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Lists the tenant's bills one page at a time, the newest bill date first. Every filter is optional and they combine:
/// the patient, the payer, the status, and a range of bill dates with both ends included.
/// </summary>
public sealed record ListBillsQuery : IPagedQuery
{
    /// <summary>Only the bills for this patient.</summary>
    public PatientId? PatientId { get; init; }

    /// <summary>Only the bills owed by this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the bills in this status.</summary>
    public BillStatus? Status { get; init; }

    /// <summary>Only the bills dated on or after this day.</summary>
    public DateOnly? BillDateFrom { get; init; }

    /// <summary>Only the bills dated on or before this day.</summary>
    public DateOnly? BillDateTo { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
