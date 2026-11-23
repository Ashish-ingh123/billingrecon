using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// What payers owe and what is held for them (US-40, BR-901), read without tracking and filtered by the current tenant.
/// The figures are the cached balances, which BR-404 keeps equal to the ledger in the same transaction.
/// </summary>
public interface IBalanceReports
{
    /// <summary>One page of the payers with a balance that is not zero, the largest net outstanding first, with the totals of all of them.</summary>
    public Task<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>> OutstandingByPayerAsync(OutstandingByPayerQuery query, CancellationToken ct);

    /// <summary>The balances of a payer the caller has found to exist; all zero when nothing was ever posted for it.</summary>
    public Task<PayerOutstandingDto> PayerBalanceAsync(PayerId payerId, string payerName, CancellationToken ct);

    /// <summary>One page of the payments that hold unapplied cash, the oldest first, with the totals of all of them.</summary>
    public Task<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>> UnappliedCashAsync(UnappliedCashQuery query, CancellationToken ct);

    /// <summary>One page of the posted credit notes that hold available credit, the oldest first, with the totals of all of them.</summary>
    public Task<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>> AvailableCreditAsync(AvailableCreditQuery query, CancellationToken ct);
}
