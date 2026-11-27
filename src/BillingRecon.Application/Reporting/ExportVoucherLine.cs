
namespace BillingRecon.Application.Reporting;

/// <summary>One line of a voucher: a ledger, and an amount on one side.</summary>
/// <param name="Ledger">The ledger.</param>
/// <param name="Debit">The debit, or zero.</param>
/// <param name="Credit">The credit, or zero.</param>
public sealed record ExportVoucherLine(ExportLedger Ledger, decimal Debit, decimal Credit);
