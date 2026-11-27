using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// Builds an export from the vouchers and the hospital's names (ADR 0062): what each ledger receives, which names are
/// missing, and the control total beside what the ledger itself says moved.
/// </summary>
public sealed class AccountingExportBuilder(IAccountingExportReports reports, IQueryHandler<GetAccountingLedgersQuery, AccountingLedgersDto> ledgerNames)
{
    /// <summary>The export of the period; the names it lacks are in <c>Summary.Missing</c>.</summary>
    public async Task<Result<AccountingExport>> BuildAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (AccountingExportPeriod.Check(from, to) is { } invalid)
        {
            return invalid;
        }

        var named = await ledgerNames.HandleAsync(new GetAccountingLedgersQuery(), ct);
        if (named.IsFailure)
        {
            return named.Error;
        }

        var read = await reports.VouchersAsync(from!.Value, to!.Value, ct);
        var totals = read.Vouchers
            .SelectMany(voucher => voucher.Lines)
            .GroupBy(line => line.Ledger)
            .OrderBy(group => group.Key)
            .Select(group => new ExportLedgerTotalDto(group.Key, NameOf(named.Value, group.Key), group.Sum(line => line.Debit), group.Sum(line => line.Credit)))
            .ToList();

        var summary = new AccountingExportSummaryDto(
            from.Value,
            to.Value,
            read.Vouchers.Count,
            read.Vouchers.Count(voucher => voucher.Type == ExportVoucherType.Receipt),
            read.Vouchers.Count(voucher => voucher.Type == ExportVoucherType.Payment),
            read.Vouchers.Count(voucher => voucher.Type == ExportVoucherType.Journal),
            totals.Sum(total => total.Debit),
            read.LedgerDebits,
            totals,
            [.. totals.Where(total => total.Name is null).Select(total => total.Ledger)]);

        var names = totals.Where(total => total.Name is not null).ToDictionary(total => total.Ledger, total => total.Name!);

        return new AccountingExport(summary, read.Vouchers, names);
    }

    private static string? NameOf(AccountingLedgersDto names, ExportLedger ledger) => ledger switch
    {
        ExportLedger.Receivable => names.Receivable,
        ExportLedger.UnappliedCash => names.UnappliedCash,
        ExportLedger.CreditAvailable => names.CreditAvailable,
        ExportLedger.Revenue => names.Revenue,
        ExportLedger.TaxOutput => names.TaxOutput,
        ExportLedger.Adjustments => names.Adjustments,
        ExportLedger.OpeningBalances => names.OpeningBalances,
        ExportLedger.Cash => names.Cash,
        ExportLedger.Card => names.Card,
        ExportLedger.Upi => names.Upi,
        ExportLedger.BankTransfer => names.BankTransfer,
        ExportLedger.Cheque => names.Cheque,
        _ => throw new ArgumentOutOfRangeException(nameof(ledger), ledger, "The ledger is not one the enum defines."),
    };
}
