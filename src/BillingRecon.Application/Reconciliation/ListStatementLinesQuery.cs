using BillingRecon.Application.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Lists the tenant's statement lines one page at a time, the latest first by default. Each filter narrows the rows: a
/// channel, an import, a status, a direction, a period of transaction dates, and a search that matches part of a
/// reference or of a description.
/// </summary>
public sealed record ListStatementLinesQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063).</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["transactionDate", "amount"];

    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>Only the lines of this channel.</summary>
    public Guid? ChannelId { get; init; }

    /// <summary>Only the lines this import stored.</summary>
    public Guid? ImportId { get; init; }

    /// <summary>Unmatched, Matched or Excluded; all when null.</summary>
    public StatementLineStatus? Status { get; init; }

    /// <summary>In or Out; both when null.</summary>
    public StatementDirection? Direction { get; init; }

    /// <summary>The first transaction date, included.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last transaction date, included.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Part of a reference or of a description.</summary>
    public string? Search { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the latest first.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
