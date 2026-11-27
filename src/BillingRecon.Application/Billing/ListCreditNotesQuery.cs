using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Lists the tenant's credit notes one page at a time, the newest date first. Every filter is optional and they
/// combine: the bill, the payer and the status.
/// </summary>
public sealed record ListCreditNotesQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "creditNoteDate", "total", "availableAmount"];

    /// <summary>Only the credit notes against this bill.</summary>
    public BillId? BillId { get; init; }

    /// <summary>Only the credit notes of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the credit notes in this status.</summary>
    public CreditNoteStatus? Status { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the credit notes dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? CreditNoteDateFrom { get; init; }

    /// <summary>Only the credit notes dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? CreditNoteDateTo { get; init; }

    /// <summary>Only the credit notes whose total is this amount or more.</summary>
    public decimal? TotalFrom { get; init; }

    /// <summary>Only the credit notes whose total is this amount or less.</summary>
    public decimal? TotalTo { get; init; }

    /// <summary>Only the credit notes this user made (the id of a user).</summary>
    public UserId? CreatedBy { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
