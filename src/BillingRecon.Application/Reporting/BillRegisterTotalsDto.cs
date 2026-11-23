namespace BillingRecon.Application.Reporting;

/// <summary>
/// The bill register's totals over the whole period and filter: how many bills, what they came to, and what is still
/// owed on them. <see cref="Total"/> equals the debits to Receivable of their postings (BR-901).
/// </summary>
public sealed record BillRegisterTotalsDto(int Bills, decimal Total, decimal BalanceDue);
