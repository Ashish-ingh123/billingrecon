using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The six cards of the dashboard (US-44, BR-902), derived from the ledger by business date (ADR 0037) for the period and
/// the previous one together, the waiting requests from their instants, and the drift from the reconciliation of now.
/// </summary>
public sealed class DashboardSummaryHandler(
    IValidator<DashboardSummaryQuery> validator,
    ILedgerMovements ledger,
    IDashboardReports reports,
    IReconciliationQuery reconciliation,
    IClock clock)
    : IQueryHandler<DashboardSummaryQuery, DashboardSummaryDto>
{
    /// <inheritdoc />
    public async Task<Result<DashboardSummaryDto>> HandleAsync(DashboardSummaryQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        var resolved = DashboardPeriods.Resolve(query.From, query.To, clock.TodayIst);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var period = resolved.Value;
        var previous = DashboardPeriods.Previous(period);
        var window = new ReportPeriodDto(previous.From, period.To);
        var (notDecidedBefore, before) = DashboardSeries.ApprovalBounds(window);

        var daily = DashboardSeries.Daily(
            window,
            await ledger.BalancesBeforeAsync(window.From, ct),
            await ledger.DaysAsync(window.From, window.To, ct),
            await reports.ApprovalSpansAsync(notDecidedBefore, before, ct),
            clock.ToIstDate);
        var drift = await reconciliation.FindDriftAsync(ct);

        var current = daily.Where(day => day.Date >= period.From).ToList();
        var earlier = daily.Where(day => day.Date <= previous.To).ToList();
        var end = current[^1];
        var previousEnd = earlier[^1];

        return new DashboardSummaryDto(
            period,
            previous,
            SummaryFigureDto.Compared(current.Sum(day => day.Billed), earlier.Sum(day => day.Billed)),
            SummaryFigureDto.Compared(current.Sum(day => day.Collected), earlier.Sum(day => day.Collected)),
            SummaryFigureDto.Compared(end.OutstandingDues, previousEnd.OutstandingDues),
            SummaryFigureDto.Compared(end.UnappliedCash, previousEnd.UnappliedCash),
            SummaryFigureDto.Compared(end.PendingApprovals, previousEnd.PendingApprovals),
            new SummaryFigureDto(drift.Count, null, null));
    }
}
