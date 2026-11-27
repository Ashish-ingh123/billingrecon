using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// Answers the export of a period, ready to be written as a file (ADR 0062). <b>A ledger the period uses and the
/// hospital has not named refuses it</b> (422 <c>accounting_ledger_not_mapped</c>, which names them): a file with a
/// ledger the package does not know would be refused there, line by line and less clearly. So does a period whose
/// vouchers do not add up to what the ledger says moved, which no test has ever seen and which would mean the export
/// is wrong.
/// </summary>
public sealed class AccountingExportHandler(AccountingExportBuilder builder) : IQueryHandler<AccountingExportQuery, AccountingExport>
{
    /// <inheritdoc />
    public async Task<Result<AccountingExport>> HandleAsync(AccountingExportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var built = await builder.BuildAsync(query.From, query.To, ct);
        if (built.IsFailure)
        {
            return built.Error;
        }

        var summary = built.Value.Summary;
        if (summary.Missing.Count > 0)
        {
            return CommonErrors.AccountingLedgerNotMapped([.. summary.Missing.Select(ledger => ledger.ToString())]);
        }

        if (summary.ControlTotal != summary.LedgerTotal || built.Value.Vouchers.Any(voucher => voucher.Lines.Sum(line => line.Debit) != voucher.Lines.Sum(line => line.Credit)))
        {
            throw new InvalidOperationException(
                $"The vouchers of {summary.From:yyyy-MM-dd} to {summary.To:yyyy-MM-dd} add up to {summary.ControlTotal} and the ledger's entries to {summary.LedgerTotal}, or a voucher does not balance: the export is not written.");
        }

        return built.Value;
    }
}
