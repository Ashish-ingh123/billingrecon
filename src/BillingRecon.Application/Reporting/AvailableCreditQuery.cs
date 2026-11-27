using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>One page of the posted credit notes that hold available credit (BR-901), the oldest first, with the totals of all of them.</summary>
public sealed record AvailableCreditQuery : IPagedQuery
{
    /// <summary>The columns this report is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "creditNoteDate", "availableAmount"];

    /// <summary>Only the credit notes of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the credit notes dated on or after this day.</summary>
    public DateOnly? DateFrom { get; init; }

    /// <summary>Only the credit notes dated on or before this day.</summary>
    public DateOnly? DateTo { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the report's own order.</summary>
    public ListSort? Sort { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
