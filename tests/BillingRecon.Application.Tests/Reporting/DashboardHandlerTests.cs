using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Reporting;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Reporting;

/// <summary>
/// The arithmetic of the dashboard (US-44, BR-902; ADR 0037): its period and the previous one, the change and the share
/// in percent, the figures of each day from the ledger's balances and movements, the steps of the trend, the buckets of
/// the ageing, and the reuse of the payment register for the recent payments. What the database answers is tested
/// against SQL Server (<c>DashboardApiTests</c>).
/// </summary>
public sealed class DashboardHandlerTests
{
    private static readonly DateOnly Today = TestDoubles.Today;
    private static readonly IClock Clock = IstClock();

    [Fact]
    public void BR902_the_period_is_the_last_thirty_days_unless_one_is_asked_for_and_the_previous_one_has_its_length()
    {
        var period = DashboardPeriods.Resolve(null, null, Today).Value;

        period.ShouldBe(new ReportPeriodDto(new DateOnly(2026, 9, 18), Today));
        DashboardPeriods.Days(period).ShouldBe(30);
        DashboardPeriods.Previous(period).ShouldBe(new ReportPeriodDto(new DateOnly(2026, 8, 19), new DateOnly(2026, 9, 17)));
        DashboardPeriods.Previous(new ReportPeriodDto(Today, Today)).ShouldBe(new ReportPeriodDto(Today.AddDays(-1), Today.AddDays(-1)));
        DashboardPeriods.Resolve(new DateOnly(2026, 10, 1), Today, Today).Value.ShouldBe(new ReportPeriodDto(new DateOnly(2026, 10, 1), Today));

        var future = DashboardPeriods.Resolve(Today, Today.AddDays(1), Today);
        future.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        future.Error.FieldErrors["to"].ShouldBe([DashboardPeriods.EndsAfterTodayMessage]);
    }

    [Theory]
    [InlineData(150, 100, 50)]
    [InlineData(50, 100, -50)]
    [InlineData(100, 300, -66.67)]
    [InlineData(200, 300, -33.33)]
    [InlineData(1, 8, -87.5)]
    [InlineData(0, 100, -100)]
    public void BR902_the_change_is_in_percent_of_the_previous_figure_to_two_places(decimal current, decimal previous, decimal expected)
    {
        DashboardFigures.ChangePercent(current, previous).ShouldBe(expected);
    }

    [Fact]
    public void BR902_a_change_from_nothing_has_no_percentage_and_a_share_of_nothing_is_zero()
    {
        DashboardFigures.ChangePercent(500m, 0m).ShouldBeNull();
        DashboardFigures.ChangePercent(0m, 0m).ShouldBeNull();
        DashboardFigures.Share(10m, 0m).ShouldBe(0m);
        DashboardFigures.Share(1m, 3m).ShouldBe(33.33m);
        DashboardFigures.Share(2m, 3m).ShouldBe(66.67m);
    }

    [Fact]
    public void BR902_each_day_carries_the_balances_at_its_end_and_the_requests_still_waiting_at_its_end_in_India()
    {
        var window = new ReportPeriodDto(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 17));
        var days = new[]
        {
            new LedgerDay(new DateOnly(2026, 10, 15), Billed: 716m, Collected: 0m, ReceivableChange: 716m, UnappliedCashChange: 0m),
            new LedgerDay(new DateOnly(2026, 10, 17), Billed: 0m, Collected: 500m, ReceivableChange: -400m, UnappliedCashChange: 100m),
        };

        // Asked 15 Oct 23:59 in India (18:29 UTC), decided 16 Oct 00:00 in India (15 Oct 18:30 UTC): it waited at the end of
        // the 15th only. Asked on the 16th in India and never decided: it waits at the end of the 16th and the 17th.
        var approvals = new[]
        {
            new ApprovalSpanDto(new DateTimeOffset(2026, 10, 15, 18, 29, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 15, 18, 30, 0, TimeSpan.Zero)),
            new ApprovalSpanDto(new DateTimeOffset(2026, 10, 15, 18, 30, 0, TimeSpan.Zero), null),
        };

        var daily = DashboardSeries.Daily(window, new LedgerBalances(100m, 20m, 5m), days, approvals, Clock.ToIstDate);

        daily.ShouldBe(
        [
            new DailyFiguresDto(new DateOnly(2026, 10, 15), 716m, 0m, 816m, 20m, 1),
            new DailyFiguresDto(new DateOnly(2026, 10, 16), 0m, 0m, 816m, 20m, 1),
            new DailyFiguresDto(new DateOnly(2026, 10, 17), 0m, 500m, 416m, 120m, 1),
        ]);
    }

    [Fact]
    public async Task BR902_the_summary_compares_the_period_with_the_previous_one_and_drift_has_no_history()
    {
        var ledger = Substitute.For<ILedgerMovements>();
        ledger.BalancesBeforeAsync(new DateOnly(2026, 10, 10), Arg.Any<CancellationToken>()).Returns(new LedgerBalances(1000m, 0m, 0m));
        ledger.DaysAsync(new DateOnly(2026, 10, 10), Today, Arg.Any<CancellationToken>()).Returns(
        [
            new LedgerDay(new DateOnly(2026, 10, 11), 300m, 100m, 200m, 0m),
            new LedgerDay(new DateOnly(2026, 10, 14), 150m, 0m, 150m, 0m),
            new LedgerDay(Today, 0m, 250m, -100m, 150m),
        ]);
        var reports = Reports();
        var reconciliation = Substitute.For<IReconciliationQuery>();
        reconciliation.FindDriftAsync(Arg.Any<CancellationToken>()).Returns([new DriftRow(DriftIdentity.BillBalance, Guid.NewGuid(), "BIL/26-27/000001", 1m, 0m)]);
        var handler = new DashboardSummaryHandler(new DashboardSummaryQueryValidator(), ledger, reports, reconciliation, Clock);

        // This week (14 to 17 October) against the week before (10 to 13 October).
        var summary = (await handler.HandleAsync(new DashboardSummaryQuery { From = new DateOnly(2026, 10, 14), To = Today }, CancellationToken.None)).Value;

        summary.PreviousPeriod.ShouldBe(new ReportPeriodDto(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 13)));
        summary.TotalBilled.ShouldBe(new SummaryFigureDto(150m, 300m, -50m));
        summary.AmountCollected.ShouldBe(new SummaryFigureDto(250m, 100m, 150m));
        summary.OutstandingDues.ShouldBe(new SummaryFigureDto(1250m, 1200m, 4.17m));
        summary.UnappliedCash.ShouldBe(new SummaryFigureDto(150m, 0m, null));
        summary.PendingApprovals.ShouldBe(new SummaryFigureDto(0m, 0m, null));
        summary.ReconciliationDrift.ShouldBe(new SummaryFigureDto(1m, null, null));
    }

    [Fact]
    public async Task BR902_a_dashboard_of_a_period_that_ends_after_today_or_runs_too_long_reads_nothing()
    {
        var ledger = Substitute.For<ILedgerMovements>();
        var handler = new DashboardSummaryHandler(new DashboardSummaryQueryValidator(), ledger, Reports(), Substitute.For<IReconciliationQuery>(), Clock);

        var future = await handler.HandleAsync(new DashboardSummaryQuery { From = Today, To = Today.AddDays(1) }, CancellationToken.None);
        var tooLong = await handler.HandleAsync(new DashboardSummaryQuery { From = Today.AddDays(-400), To = Today }, CancellationToken.None);
        var half = await handler.HandleAsync(new DashboardSummaryQuery { From = Today }, CancellationToken.None);

        foreach (var result in new[] { future, tooLong, half })
        {
            result.Error.FieldErrors.Keys.ShouldBe(["to"]);
        }

        ledger.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task BR902_the_trend_steps_by_day_up_to_31_days_and_by_week_from_monday_beyond_with_running_totals()
    {
        var ledger = Substitute.For<ILedgerMovements>();
        ledger.BalancesBeforeAsync(default, default).ReturnsForAnyArgs(new LedgerBalances(0m, 0m, 0m));
        ledger.DaysAsync(default, default, default).ReturnsForAnyArgs(
        [
            new LedgerDay(new DateOnly(2026, 9, 16), 100m, 10m, 90m, 0m),
            new LedgerDay(new DateOnly(2026, 9, 21), 200m, 20m, 180m, 0m),
            new LedgerDay(Today, 300m, 30m, 270m, 0m),
        ]);
        var handler = new DashboardTrendHandler(new DashboardTrendQueryValidator(), ledger, Reports(), Clock);

        // 16 September (a Wednesday) to 17 October: 32 days, so per week.
        var weekly = (await handler.HandleAsync(new DashboardTrendQuery { From = new DateOnly(2026, 9, 16), To = Today }, CancellationToken.None)).Value;
        var daily = (await handler.HandleAsync(new DashboardTrendQuery { From = new DateOnly(2026, 10, 15), To = Today }, CancellationToken.None)).Value;

        weekly.Interval.ShouldBe(TrendInterval.Week);
        weekly.Points[0].ShouldBe(new TrendPointDto(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 20), 100m, 10m, 100m, 10m));
        weekly.Points[1].ShouldBe(new TrendPointDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 200m, 20m, 300m, 30m));
        weekly.Points[^1].ShouldBe(new TrendPointDto(new DateOnly(2026, 10, 12), Today, 300m, 30m, 600m, 60m));
        weekly.Points.Count.ShouldBe(5);
        weekly.Daily.Count.ShouldBe(32);
        weekly.Daily[^1].OutstandingDues.ShouldBe(540m);
        daily.Interval.ShouldBe(TrendInterval.Day);
        daily.Points.Select(point => (point.From, point.To)).ShouldBe([(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15)), (new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16)), (Today, Today)]);
    }

    [Fact]
    public async Task BR902_a_bill_due_today_or_not_yet_due_is_in_0_30_and_the_buckets_add_up_to_what_is_owed()
    {
        var reports = Reports();
        reports.OpenBillsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new OpenBillDto(Today.AddDays(15), 100m),
            new OpenBillDto(Today, 50m),
            new OpenBillDto(Today.AddDays(-30), 25m),
            new OpenBillDto(Today.AddDays(-31), 10m),
            new OpenBillDto(Today.AddDays(-90), 5m),
            new OpenBillDto(Today.AddDays(-91), 1m),
        ]);
        var handler = new DashboardAgeingHandler(reports, Clock);

        var ageing = (await handler.HandleAsync(new DashboardAgeingQuery(), CancellationToken.None)).Value;

        ageing.Buckets.Select(bucket => (bucket.Bucket, bucket.Bills, bucket.Amount)).ShouldBe([("0-30", 3, 175m), ("31-60", 1, 10m), ("61-90", 1, 5m), ("90+", 1, 1m)]);
        (ageing.AsOf, ageing.Bills, ageing.Total).ShouldBe((Today, 6, 191m));
        ageing.Buckets.Sum(bucket => bucket.Amount).ShouldBe(ageing.Total);
        ageing.Buckets[0].Share.ShouldBe(91.62m);
    }

    [Fact]
    public async Task BR902_the_recent_payments_are_the_payment_register_of_the_period_newest_first_five_of_them()
    {
        var register = Substitute.For<IQueryHandler<PaymentRegisterQuery, ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>>();
        register.HandleAsync(default!, default).ReturnsForAnyArgs(new ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>(null, [], 1, 5, 0, new PaymentRegisterTotalsDto(0, 0m, 0m)));
        var handler = new DashboardRecentPaymentsHandler(new DashboardRecentPaymentsQueryValidator(), register, Clock);

        (await handler.HandleAsync(new DashboardRecentPaymentsQuery(), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await register.Received(1).HandleAsync(
            Arg.Is<PaymentRegisterQuery>(query => query.From == new DateOnly(2026, 9, 18) && query.To == Today && query.Order == ReportOrder.NewestFirst && query.Page == 1 && query.PageSize == 5),
            Arg.Any<CancellationToken>());
    }

    private static IDashboardReports Reports()
    {
        var reports = Substitute.For<IDashboardReports>();
        reports.OpenBillsAsync(default).ReturnsForAnyArgs([]);
        reports.ApprovalSpansAsync(default, default, default).ReturnsForAnyArgs([]);

        return reports;
    }

    // Today of the tests, and India's date of an instant (UTC+05:30, no daylight saving).
    private static IClock IstClock()
    {
        var clock = TestDoubles.ClockAt(Today);
        clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(call => DateOnly.FromDateTime(call.Arg<DateTimeOffset>().ToOffset(new TimeSpan(5, 30, 0)).DateTime));

        return clock;
    }
}
