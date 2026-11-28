using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The query string of <c>GET /api/v1/statement-lines</c>: <c>channelId</c>, <c>importId</c>, <c>status</c>,
/// <c>direction</c>, <c>from</c>, <c>to</c>, <c>search</c>, <c>sort</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListStatementLinesRequest
{
    /// <summary>Only the lines of this channel.</summary>
    public Guid? ChannelId { get; init; }

    /// <summary>Only the lines this import stored.</summary>
    public Guid? ImportId { get; init; }

    /// <summary>Unmatched, Matched or Excluded; all when left out.</summary>
    public StatementLineStatus? Status { get; init; }

    /// <summary>In or Out; both when left out.</summary>
    public StatementDirection? Direction { get; init; }

    /// <summary>The first transaction date, included.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last transaction date, included.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Part of a reference or of a description.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// The order: <c>transactionDate</c> or <c>amount</c>, with <c>-</c> in front for the latest or largest first. The
    /// latest first when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Lines per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public Result<ListStatementLinesQuery> ToQuery()
    {
        var read = new FilterReader();
        var order = read.Sort(Sort, ListStatementLinesQuery.Sorts);

        return read.Answer(new ListStatementLinesQuery
        {
            ChannelId = ChannelId,
            ImportId = ImportId,
            Status = Status,
            Direction = Direction,
            From = From,
            To = To,
            Search = Search,
            Order = order,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
        });
    }
}
