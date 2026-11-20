using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>One page of the payments of the tenant, the newest payment date first. The filters combine.</summary>
public sealed record ListPaymentsQuery : IPagedQuery
{
    /// <summary>Only payments of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only payments received this way.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>Only payments in this state.</summary>
    public PaymentStatus? Status { get; init; }

    /// <summary>Only payments dated on or after this day.</summary>
    public DateOnly? PaymentDateFrom { get; init; }

    /// <summary>Only payments dated on or before this day.</summary>
    public DateOnly? PaymentDateTo { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
