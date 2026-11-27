using BillingRecon.Application.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the payers that owe something or have something held for them (BR-901), the largest net outstanding first,
/// with the totals of all of them.
/// </summary>
public sealed record OutstandingByPayerQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["name", "receivable", "netOutstanding"];

    /// <summary>The longest search accepted.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>Only payers whose name contains this text.</summary>
    public string? Search { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Sort { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
