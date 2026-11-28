using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;

namespace BillingRecon.Api.Reconciliation;

/// <summary>The query string of <c>GET /api/v1/statement-imports</c>: <c>channelId</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListStatementImportsRequest
{
    /// <summary>Only the imports of this channel.</summary>
    public Guid? ChannelId { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Imports per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListStatementImportsQuery ToQuery() => new()
    {
        ChannelId = ChannelId,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
