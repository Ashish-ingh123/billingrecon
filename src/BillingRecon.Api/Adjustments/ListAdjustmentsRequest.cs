using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Adjustments;

/// <summary>The query string of <c>GET /api/v1/adjustments</c>: <c>billId</c>, <c>payerId</c>, <c>status</c>, <c>kind</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListAdjustmentsRequest
{
    /// <summary>Only the adjustments of this bill.</summary>
    public Guid? BillId { get; init; }

    /// <summary>Only the adjustments of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Draft, PendingApproval, Posted or Voided; all when left out.</summary>
    public AdjustmentStatus? Status { get; init; }

    /// <summary>Discount, WriteOff or Rounding; all when left out.</summary>
    public AdjustmentKind? Kind { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListAdjustmentsQuery ToQuery() => new()
    {
        BillId = BillId is { } billId ? new BillId(billId) : null,
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Status = Status,
        Kind = Kind,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
