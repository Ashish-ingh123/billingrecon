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
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "adjustmentDate", "amount"];

    /// <summary>Only the adjustments of this bill.</summary>
    public BillId? BillId { get; init; }

    /// <summary>Only the adjustments of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the adjustments in this status.</summary>
    public AdjustmentStatus? Status { get; init; }

    /// <summary>Only the adjustments of this kind.</summary>
    public AdjustmentKind? Kind { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the adjustments dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? AdjustmentDateFrom { get; init; }

    /// <summary>Only the adjustments dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? AdjustmentDateTo { get; init; }

    /// <summary>Only the adjustments of this amount or more.</summary>
    public decimal? AmountFrom { get; init; }

    /// <summary>Only the adjustments of this amount or less.</summary>
    public decimal? AmountTo { get; init; }

    /// <summary>Only the adjustments this user made (the id of a user).</summary>
    public UserId? CreatedBy { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
