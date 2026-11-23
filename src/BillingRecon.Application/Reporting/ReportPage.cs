namespace BillingRecon.Application.Reporting;

/// <summary>
/// One page of a report (docs/api.md section 8.15): the items of the page in a stable order, the paging of a list, and
/// the totals of every page together, so that a report can be held against the ledger (BR-901, ADR 0036). A register
/// also says which period it covers; the other reports have none.
/// </summary>
/// <typeparam name="TItem">The type of the items.</typeparam>
/// <typeparam name="TTotals">The type of the totals.</typeparam>
public sealed record ReportPage<TItem, TTotals>(
    ReportPeriodDto? Period,
    IReadOnlyList<TItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    TTotals Totals);
