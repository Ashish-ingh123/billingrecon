
namespace BillingRecon.Application.Reporting;

/// <summary>What the query service reads for an export: the vouchers, and what the ledger itself says moved.</summary>
/// <param name="Vouchers">The vouchers of the period, in the order they were posted.</param>
/// <param name="LedgerDebits">The debits of every ledger entry of the period, read on their own and added up.</param>
/// <param name="LedgerCredits">The credits of every ledger entry of the period, read on their own and added up.</param>
public sealed record ExportVouchers(IReadOnlyList<ExportVoucher> Vouchers, decimal LedgerDebits, decimal LedgerCredits);
