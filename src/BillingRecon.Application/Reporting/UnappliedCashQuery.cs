using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>One page of the payments that hold unapplied cash (BR-304, BR-901), the oldest first, with the totals of all of them.</summary>
public sealed record UnappliedCashQuery : IPagedQuery
{
    /// <summary>The columns this report is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["number", "paymentDate", "unappliedAmount"];

    /// <summary>Only the payments of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the payments dated on or after this day.</summary>
    public DateOnly? DateFrom { get; init; }

    /// <summary>Only the payments dated on or before this day.</summary>
    public DateOnly? DateTo { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the report's own order.</summary>
    public ListSort? Sort { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
