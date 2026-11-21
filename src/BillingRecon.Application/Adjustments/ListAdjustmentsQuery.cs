using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// Lists the tenant's adjustments one page at a time, the newest date first. Every filter is optional and they
/// combine: the bill, the payer, the status and the kind.
/// </summary>
public sealed record ListAdjustmentsQuery : IPagedQuery
{
    /// <summary>Only the adjustments of this bill.</summary>
    public BillId? BillId { get; init; }

    /// <summary>Only the adjustments of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the adjustments in this status.</summary>
    public AdjustmentStatus? Status { get; init; }

    /// <summary>Only the adjustments of this kind.</summary>
    public AdjustmentKind? Kind { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
