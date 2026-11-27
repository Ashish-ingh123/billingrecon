
namespace BillingRecon.Application.Reporting;

/// <summary>An export ready to be written: the vouchers, the name of every ledger they use, and the summary.</summary>
/// <param name="Summary">The control totals.</param>
/// <param name="Vouchers">The vouchers.</param>
/// <param name="Names">The name of each ledger the vouchers use.</param>
public sealed record AccountingExport(AccountingExportSummaryDto Summary, IReadOnlyList<ExportVoucher> Vouchers, IReadOnlyDictionary<ExportLedger, string> Names);
