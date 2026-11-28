using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Api.Reconciliation;

/// <summary>The query string of <c>GET /api/v1/settlement-channels</c>: <c>kind</c>, <c>status</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListSettlementChannelsRequest
{
    /// <summary>Bank or Gateway; both when left out.</summary>
    public SettlementChannelKind? Kind { get; init; }

    /// <summary>Active or Inactive; both when left out.</summary>
    public SettlementChannelStatus? Status { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Channels per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListSettlementChannelsQuery ToQuery() => new()
    {
        Kind = Kind,
        Status = Status,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
