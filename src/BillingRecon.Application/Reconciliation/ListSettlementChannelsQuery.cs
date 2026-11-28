using BillingRecon.Application.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Lists the tenant's settlement channels one page at a time, by name; optionally of one kind or one status.</summary>
public sealed record ListSettlementChannelsQuery : IPagedQuery
{
    /// <summary>Bank or Gateway; both when null.</summary>
    public SettlementChannelKind? Kind { get; init; }

    /// <summary>Active or Inactive; both when null.</summary>
    public SettlementChannelStatus? Status { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
