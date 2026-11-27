
namespace BillingRecon.Application.Reporting;

/// <summary>Reads the postings of a period as vouchers (ADR 0062). It never writes.</summary>
public interface IAccountingExportReports
{
    /// <summary>The vouchers of the business dates <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public Task<ExportVouchers> VouchersAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
