using BillingRecon.Application.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Lists the tenant's imports one page at a time, the latest first; optionally those of one channel.</summary>
public sealed record ListStatementImportsQuery : IPagedQuery
{
    /// <summary>Only the imports of this channel.</summary>
    public Guid? ChannelId { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
