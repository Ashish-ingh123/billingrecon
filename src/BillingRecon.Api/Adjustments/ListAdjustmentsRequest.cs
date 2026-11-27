using BillingRecon.Api.Common;
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

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the adjustments dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public string? AdjustmentDateFrom { get; init; }

    /// <summary>Only the adjustments dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public string? AdjustmentDateTo { get; init; }

    /// <summary>Only the adjustments of this amount or more.</summary>
    public string? AmountFrom { get; init; }

    /// <summary>Only the adjustments of this amount or less.</summary>
    public string? AmountTo { get; init; }

    /// <summary>Only the adjustments this user made (the id of a user).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>number</c>, <c>adjustmentDate</c>, <c>amount</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public Result<ListAdjustmentsQuery> ToQuery()
    {
        var read = new FilterReader();
        var adjustmentDateFrom = read.Date(AdjustmentDateFrom, "adjustmentDateFrom");
        var adjustmentDateTo = read.Date(AdjustmentDateTo, "adjustmentDateTo");
        var amountFrom = read.Amount(AmountFrom, "amountFrom");
        var amountTo = read.Amount(AmountTo, "amountTo");
        var createdBy = read.Id(CreatedBy, "createdBy");
        read.InOrder(adjustmentDateFrom, adjustmentDateTo, "adjustmentDateTo");
        read.InOrder(amountFrom, amountTo, "amountTo");
        var order = read.Sort(Sort, ListAdjustmentsQuery.Sorts);

        return read.Answer(new ListAdjustmentsQuery
        {
            BillId = BillId is { } billId ? new BillId(billId) : null,
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Status = Status,
            Kind = Kind,
            Search = Search,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            AdjustmentDateFrom = adjustmentDateFrom,
            AdjustmentDateTo = adjustmentDateTo,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
            CreatedBy = createdBy is { } createdById ? new UserId(createdById) : null,
            Order = order,
        });
    }
}
