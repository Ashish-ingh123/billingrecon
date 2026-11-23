using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The trend of the dashboard (US-44, BR-902): the figures of every day of the period from the ledger by business date
/// (ADR 0037), gathered per day or per week from Monday, with running totals from the period's start.
/// </summary>
public sealed class DashboardTrendHandler(IValidator<DashboardTrendQuery> validator, ILedgerMovements ledger, IDashboardReports reports, IClock clock)
    : IQueryHandler<DashboardTrendQuery, DashboardTrendDto>
{
    /// <inheritdoc />
    public async Task<Result<DashboardTrendDto>> HandleAsync(DashboardTrendQuery query, CancellationToken ct)
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
        var interval = query.Interval ?? (DashboardPeriods.Days(period) <= DashboardTrendQuery.DailyUpToDays ? TrendInterval.Day : TrendInterval.Week);
        var (notDecidedBefore, before) = DashboardSeries.ApprovalBounds(period);

        var daily = DashboardSeries.Daily(
            period,
            await ledger.BalancesBeforeAsync(period.From, ct),
            await ledger.DaysAsync(period.From, period.To, ct),
            await reports.ApprovalSpansAsync(notDecidedBefore, before, ct),
            clock.ToIstDate);

        return new DashboardTrendDto(period, interval, Points(daily, interval), daily);
    }

    private static TrendPointDto[] Points(IReadOnlyList<DailyFiguresDto> daily, TrendInterval interval) => [.. Steps(daily, interval)];

    // Each day or week of the period, oldest first, with the running totals from the period's start.
    private static IEnumerable<TrendPointDto> Steps(IReadOnlyList<DailyFiguresDto> daily, TrendInterval interval)
    {
        var cumulativeBilled = 0m;
        var cumulativeCollected = 0m;
        foreach (var step in daily.GroupBy(day => interval == TrendInterval.Day ? day.Date : Monday(day.Date)))
        {
            var billed = step.Sum(day => day.Billed);
            var collected = step.Sum(day => day.Collected);
            cumulativeBilled += billed;
            cumulativeCollected += collected;

            yield return new TrendPointDto(step.First().Date, step.Last().Date, billed, collected, cumulativeBilled, cumulativeCollected);
        }
    }

    // The Monday of the week of the date: Sunday ends a week.
    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
