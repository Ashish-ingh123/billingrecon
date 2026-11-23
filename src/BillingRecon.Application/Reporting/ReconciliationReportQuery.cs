using BillingRecon.Application.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of the reconciliation (US-41, BR-404, BR-901): every place where a cached balance of the current tenant
/// differs from the ledger, or the ledger or a number series breaks one of its identities (docs/database.md sections
/// 5.5 and 5.6). An empty report means none.
/// </summary>
public sealed record ReconciliationReportQuery : IPagedQuery
{
    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
