using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>One page of the payments of the tenant, the newest payment date first. The filters combine.</summary>
public sealed record ListPaymentsQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "paymentDate", "amount", "unappliedAmount"];

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

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the payments of this amount or more.</summary>
    public decimal? AmountFrom { get; init; }

    /// <summary>Only the payments of this amount or less.</summary>
    public decimal? AmountTo { get; init; }

    /// <summary>Only the payments this user made (the id of a user).</summary>
    public UserId? CreatedBy { get; init; }

    /// <summary>Only the payments of this origin: Standard, or Opening for the advances brought in at the cut-over.</summary>
    public DocumentOrigin? Origin { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
