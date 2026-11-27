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
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "billDate", "total", "balanceDue"];

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

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the bills whose total is this amount or more.</summary>
    public decimal? TotalFrom { get; init; }

    /// <summary>Only the bills whose total is this amount or less.</summary>
    public decimal? TotalTo { get; init; }

    /// <summary>Only the bills that still owe this amount or more.</summary>
    public decimal? BalanceDueFrom { get; init; }

    /// <summary>Only the bills that still owe this amount or less.</summary>
    public decimal? BalanceDueTo { get; init; }

    /// <summary>Only the bills this user made (the id of a user).</summary>
    public UserId? CreatedBy { get; init; }

    /// <summary>Only the bills of this origin: Standard, or Opening for those brought in at the cut-over.</summary>
    public DocumentOrigin? Origin { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
