using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The query string of <c>GET /api/v1/payments</c>: <c>payerId</c>, <c>method</c>, <c>status</c>,
/// <c>paymentDateFrom</c>, <c>paymentDateTo</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListPaymentsRequest
{
    /// <summary>Only payments of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Only payments received this way.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>Only payments in this state.</summary>
    public PaymentStatus? Status { get; init; }

    /// <summary>Only payments dated on or after this day, <c>yyyy-MM-dd</c>.</summary>
    public DateOnly? PaymentDateFrom { get; init; }

    /// <summary>Only payments dated on or before this day, <c>yyyy-MM-dd</c>.</summary>
    public DateOnly? PaymentDateTo { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListPaymentsQuery ToQuery() => new()
    {
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Method = Method,
        Status = Status,
        PaymentDateFrom = PaymentDateFrom,
        PaymentDateTo = PaymentDateTo,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
