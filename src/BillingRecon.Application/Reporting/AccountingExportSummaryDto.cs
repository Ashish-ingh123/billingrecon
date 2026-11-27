
namespace BillingRecon.Application.Reporting;

/// <summary>
/// What an export of a period holds, before the file is made (ADR 0062): the control totals the accountant compares
/// with what the package says it imported.
/// </summary>
/// <param name="From">The first business date.</param>
/// <param name="To">The last business date.</param>
/// <param name="Vouchers">How many vouchers.</param>
/// <param name="Receipts">How many of them are receipts.</param>
/// <param name="Payments">How many are payments.</param>
/// <param name="Journals">How many are journals.</param>
/// <param name="ControlTotal">The debits of every voucher, which equal their credits.</param>
/// <param name="LedgerTotal">The debits of our ledger for the period, read on their own and added up: equal to <paramref name="ControlTotal"/>.</param>
/// <param name="Ledgers">What each ledger receives; only the ledgers the period uses.</param>
/// <param name="Missing">The ledgers the period uses and the hospital has not named: the file is refused until they are.</param>
public sealed record AccountingExportSummaryDto(
    DateOnly From,
    DateOnly To,
    int Vouchers,
    int Receipts,
    int Payments,
    int Journals,
    decimal ControlTotal,
    decimal LedgerTotal,
    IReadOnlyList<ExportLedgerTotalDto> Ledgers,
    IReadOnlyList<ExportLedger> Missing);
