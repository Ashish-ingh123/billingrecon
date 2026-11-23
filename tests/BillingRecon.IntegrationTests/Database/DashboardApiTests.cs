using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The dashboard (US-44, BR-902; ADR 0037) after three months of one hospital, the clock walked through them so that
/// every document is made on its own day: old bills in each bucket of the ageing, and this week (11 to 17 October)
/// against the week before (4 to 10 October), with a payment on each side of midnight in India and two reversals asked
/// for, one decided in this week. Every figure is held against the ledger summed in SQL here, and the reconciliation
/// after the test finds no drift.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DashboardApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly ReportPeriodDto Week = new(new DateOnly(2026, 10, 11), Today);
    private static readonly ReportPeriodDto WeekBefore = new(new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 10));
    private static readonly TimeSpan India = new(5, 30, 0);

    [Fact]
    public async Task US44_the_summary_of_a_week_against_the_week_before_equals_the_ledger()
    {
        using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;

        var summary = await scenario.ReadAsync<DashboardSummaryDto>($"summary?from={Week.From:yyyy-MM-dd}&to={Week.To:yyyy-MM-dd}");

        (summary.Period, summary.PreviousPeriod).ShouldBe((Week, WeekBefore));
        summary.TotalBilled.ShouldBe(new SummaryFigureDto(1000m, 500m, 100m));
        summary.AmountCollected.ShouldBe(new SummaryFigureDto(520m, 300m, 73.33m));
        summary.OutstandingDues.ShouldBe(new SummaryFigureDto(1050m, 450m, 133.33m));
        summary.UnappliedCash.ShouldBe(new SummaryFigureDto(120m, 100m, 20m));
        summary.PendingApprovals.ShouldBe(new SummaryFigureDto(1m, 1m, 0m));
        summary.ReconciliationDrift.ShouldBe(new SummaryFigureDto(0m, null, null));

        // Each figure, and its previous one, as the ledger and the requests give them.
        foreach (var (period, billed, collected, outstanding, unapplied, pending) in new[]
        {
            (Week, summary.TotalBilled.Value, summary.AmountCollected.Value, summary.OutstandingDues.Value, summary.UnappliedCash.Value, summary.PendingApprovals.Value),
            (WeekBefore, summary.TotalBilled.Previous!.Value, summary.AmountCollected.Previous!.Value, summary.OutstandingDues.Previous!.Value, summary.UnappliedCash.Previous!.Value, summary.PendingApprovals.Previous!.Value),
        })
        {
            billed.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND p.[Kind] = 'BillPosted' AND e.[Account] = 'Receivable' AND p.[BusinessDate] BETWEEN {period.From} AND {period.To}"));
            collected.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND p.[Kind] = 'PaymentRecorded' AND e.[Account] = 'Cash' AND p.[BusinessDate] BETWEEN {period.From} AND {period.To}"));
            outstanding.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND e.[Account] = 'Receivable' AND p.[BusinessDate] <= {period.To}"));
            unapplied.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND e.[Account] = 'UnappliedCash' AND p.[BusinessDate] <= {period.To}"));

            // Waiting at the end of the last day in India: asked for before that midnight and not decided before it.
            var end = new DateTimeOffset(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), India);
            pending.ShouldBe(await LedgerAsync($"SELECT CAST(COUNT(*) AS decimal(18,2)) AS [Value] FROM dbo.ApprovalRequests WHERE [TenantId] = {tenant} AND [RequestedAtUtc] < {end} AND ([DecidedAtUtc] IS NULL OR [DecidedAtUtc] >= {end})"));
        }
    }

    [Fact]
    public async Task BR902_a_payment_a_minute_before_midnight_in_India_is_the_day_before_and_one_a_minute_after_is_the_next()
    {
        using var scenario = await ScenarioAsync();

        var trend = await scenario.ReadAsync<DashboardTrendDto>($"trend?from={WeekBefore.From:yyyy-MM-dd}&to={Week.To:yyyy-MM-dd}&interval=Day");

        // 10 October 23:59 in India: the UPI payment of 100.00, and its reversal asked for. 11 October 00:01: the cheque of 70.00.
        var tenth = trend.Daily.Single(day => day.Date == new DateOnly(2026, 10, 10));
        var eleventh = trend.Daily.Single(day => day.Date == new DateOnly(2026, 10, 11));
        (tenth.Collected, tenth.PendingApprovals).ShouldBe((100m, 1));
        (eleventh.Collected, eleventh.PendingApprovals).ShouldBe((70m, 1));
        trend.Daily.Single(day => day.Date == new DateOnly(2026, 10, 12)).PendingApprovals.ShouldBe(0, "the reversal was approved on the 12th");
        trend.Daily.Single(day => day.Date == new DateOnly(2026, 10, 9)).PendingApprovals.ShouldBe(0, "it was asked for on the 10th");
    }

    [Fact]
    public async Task US44_the_trend_of_the_week_runs_billed_against_collected_and_its_last_day_closes_on_the_summary()
    {
        using var scenario = await ScenarioAsync();

        var trend = await scenario.ReadAsync<DashboardTrendDto>($"trend?from={Week.From:yyyy-MM-dd}&to={Week.To:yyyy-MM-dd}");
        var summary = await scenario.ReadAsync<DashboardSummaryDto>($"summary?from={Week.From:yyyy-MM-dd}&to={Week.To:yyyy-MM-dd}");

        (trend.Period, trend.Interval, trend.Points.Count, trend.Daily.Count).ShouldBe((Week, TrendInterval.Day, 7, 7));
        trend.Points.Select(point => point.Billed).ShouldBe([0m, 0m, 1000m, 0m, 0m, 0m, 0m]);
        trend.Points.Select(point => point.Collected).ShouldBe([70m, 0m, 0m, 400m, 0m, 0m, 50m]);
        (trend.Points[^1].CumulativeBilled, trend.Points[^1].CumulativeCollected).ShouldBe((summary.TotalBilled.Value, summary.AmountCollected.Value));
        trend.Daily.Select(day => day.PendingApprovals).ShouldBe([1, 0, 0, 0, 0, 1, 1]);
        (trend.Daily[^1].OutstandingDues, trend.Daily[^1].UnappliedCash).ShouldBe((summary.OutstandingDues.Value, summary.UnappliedCash.Value));
    }

    [Fact]
    public async Task US44_the_ageing_puts_a_bill_due_today_in_0_30_and_its_buckets_add_up_to_the_outstanding_dues()
    {
        using var scenario = await ScenarioAsync();

        var ageing = await scenario.ReadAsync<AgeingDto>("ageing");

        ageing.AsOf.ShouldBe(Today);
        ageing.Buckets.Select(bucket => (bucket.Bucket, bucket.Bills, bucket.Amount)).ShouldBe([("0-30", 2, 900m), ("31-60", 1, 30m), ("61-90", 1, 70m), ("90+", 1, 50m)]);
        ageing.Buckets.Select(bucket => bucket.Share).ShouldBe([85.71m, 2.86m, 6.67m, 4.76m]);
        ageing.Total.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {scenario.TenantId} AND e.[Account] = 'Receivable'"));
        ageing.Buckets.Sum(bucket => bucket.Amount).ShouldBe(ageing.Total);
    }

    [Fact]
    public async Task US44_the_four_lists_are_the_reports_of_6_1_and_6_2_cut_to_the_panel()
    {
        using var scenario = await ScenarioAsync();

        var payers = await scenario.ReadAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>("top-payers");
        var payments = await scenario.ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>($"recent-payments?from={Week.From:yyyy-MM-dd}&to={Week.To:yyyy-MM-dd}");
        var pending = await scenario.ReadAsync<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>("pending-approvals");
        var drift = await scenario.ReadAsync<ReportPage<DriftRowDto, ReconciliationTotalsDto>>("drift");

        payers.Items.Select(payer => (payer.PayerId, payer.NetOutstanding)).ShouldBe([(scenario.Second.Id, 720m), (scenario.First.Id, 210m)]);
        payments.Items.Select(payment => (payment.Method, payment.Amount, payment.PaymentDate)).ShouldBe(
        [
            (ReportPaymentMethod.Cash, 50m, Today),
            (ReportPaymentMethod.Card, 400m, new DateOnly(2026, 10, 14)),
            (ReportPaymentMethod.Cheque, 70m, new DateOnly(2026, 10, 11)),
        ]);
        (payments.PageSize, payments.Totals.Amount).ShouldBe((5, 520m));
        pending.Items.ShouldHaveSingleItem().Amount.ShouldBe(400m);
        (pending.PageSize, drift.PageSize, drift.TotalCount).ShouldBe((3, 3, 0));
    }

    [Fact]
    public async Task US44_another_hospital_s_documents_are_in_no_figure_and_a_new_hospital_s_dashboard_is_zeros()
    {
        using var scenario = await ScenarioAsync();
        var other = await NewApiTenantAsync();
        using var otherViewer = scenario.Factory.CreateClientFor(other, "Viewer");

        var ours = await scenario.ReadAsync<DashboardSummaryDto>("summary");
        var theirs = await ReadAsync<DashboardSummaryDto>(otherViewer, "summary");
        var theirAgeing = await ReadAsync<AgeingDto>(otherViewer, "ageing");
        var theirTrend = await ReadAsync<DashboardTrendDto>(otherViewer, "trend");

        ours.Period.ShouldBe(new ReportPeriodDto(new DateOnly(2026, 9, 18), Today));
        ours.TotalBilled.Value.ShouldBe(1500m);
        foreach (var figure in new[] { theirs.TotalBilled, theirs.AmountCollected, theirs.OutstandingDues, theirs.UnappliedCash, theirs.PendingApprovals })
        {
            figure.ShouldBe(new SummaryFigureDto(0m, 0m, null));
        }

        theirs.ReconciliationDrift.ShouldBe(new SummaryFigureDto(0m, null, null));
        (theirAgeing.Total, theirAgeing.Bills).ShouldBe((0m, 0));
        theirAgeing.Buckets.ShouldAllBe(bucket => bucket.Amount == 0m && bucket.Share == 0m && bucket.Bills == 0);
        theirTrend.Daily.ShouldAllBe(day => day.Billed == 0m && day.Collected == 0m && day.OutstandingDues == 0m && day.PendingApprovals == 0);
    }

    [Fact]
    public async Task BR902_a_period_that_ends_after_today_or_runs_more_than_366_days_is_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var viewer = factory.CreateClientFor(tenant, "Viewer");

        using var future = await viewer.GetAsync(Dashboard("summary?from=2026-10-17&to=2026-10-18"));
        using var tooLong = await viewer.GetAsync(Dashboard("trend?from=2025-10-01&to=2026-10-17"));
        using var interval = await viewer.GetAsync(Dashboard("trend?interval=Month"));

        foreach (var response in new[] { future, tooLong })
        {
            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
            problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["to"]);
        }

        await interval.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
    }

    private static Uri Dashboard(string pathAndQuery) => new($"/api/v1/dashboard/{pathAndQuery}", UriKind.Relative);

    private static async Task<T> ReadAsync<T>(HttpClient client, string pathAndQuery)
        where T : class
    {
        using var response = await client.GetAsync(Dashboard(pathAndQuery));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    private async Task<decimal> LedgerAsync(FormattableString sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQuery<decimal>(sql).SingleAsync();
    }

    // Three months of one hospital, each document made on its own day by the clock (06:30 UTC is noon in India).
    private async Task<Scenario> ScenarioAsync()
    {
        var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        var checker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        var viewer = factory.CreateClientFor(new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())), "Viewer");
        using var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (firstPatient, first) = await clerk.NewBillPartiesAsync();
        var (secondPatient, second) = await clerk.NewBillPartiesAsync();

        // Old bills, one for each later bucket of the ageing on 17 October: 108, 77 and 31 days past due.
        await On(factory, new DateOnly(2026, 7, 1), () => PostedBillAsync(clerk, secondPatient, second, 50m, new DateOnly(2026, 7, 1)));
        await On(factory, new DateOnly(2026, 8, 1), () => PostedBillAsync(clerk, secondPatient, second, 70m, new DateOnly(2026, 8, 1)));
        await On(factory, new DateOnly(2026, 9, 1), () => PostedBillAsync(clerk, firstPatient, first, 30m, new DateOnly(2026, 9, 16)));

        // The week before: a bill due on 17 October, 200.00 of it paid; 100.00 by UPI a minute before midnight, whose
        // reversal is asked for at once.
        var admission = await On(factory, new DateOnly(2026, 10, 5), () => PostedBillAsync(clerk, firstPatient, first, 500m, Today));
        await On(factory, new DateOnly(2026, 10, 6), async () => await AllocatedAsync(clerk, await RecordedAsync(clerk, first, PaymentMethod.Cash, 200m), admission.Id, 200m));
        factory.Clock.Set(new DateTimeOffset(2026, 10, 10, 18, 29, 0, TimeSpan.Zero));
        var upi = await RecordedAsync(clerk, first, PaymentMethod.UPI, 100m);
        await ReversalRequestedAsync(clerk, upi.Id);

        // This week: a cheque a minute after midnight; the UPI reversal approved; a bill of 1000.00, 400.00 of it paid by
        // card, whose reversal waits; 50.00 in cash today.
        factory.Clock.Set(new DateTimeOffset(2026, 10, 10, 18, 31, 0, TimeSpan.Zero));
        await RecordedAsync(clerk, first, PaymentMethod.Cheque, 70m);
        await On(factory, new DateOnly(2026, 10, 12), () => ReversalApprovedAsync(supervisor, upi.Id));
        var surgery = await On(factory, new DateOnly(2026, 10, 13), () => PostedBillAsync(clerk, secondPatient, second, 1000m, new DateOnly(2026, 11, 12)));
        var card = await On(factory, new DateOnly(2026, 10, 14), async () =>
        {
            var payment = await RecordedAsync(clerk, second, PaymentMethod.Card, 400m);
            await AllocatedAsync(clerk, payment, surgery.Id, 400m);
            return payment;
        });
        await On(factory, new DateOnly(2026, 10, 16), () => ReversalRequestedAsync(clerk, card.Id));
        await On(factory, Today, () => RecordedAsync(clerk, first, PaymentMethod.Cash, 50m));

        return new Scenario(factory, maker, viewer, first, second);
    }

    // Moves the clock to noon in India of the date, then does the work.
    private static async Task<T> On<T>(DatabaseApiFactory factory, DateOnly date, Func<Task<T>> work)
    {
        factory.Clock.Set(new DateTimeOffset(date.ToDateTime(new TimeOnly(6, 30)), TimeSpan.Zero));

        return await work();
    }

    private static async Task On(DatabaseApiFactory factory, DateOnly date, Func<Task> work)
    {
        factory.Clock.Set(new DateTimeOffset(date.ToDateTime(new TimeOnly(6, 30)), TimeSpan.Zero));

        await work();
    }

    // A bill of one free-text line, dated today by the clock (no date sent), due on the date given.
    private static async Task<BillDto> PostedBillAsync(HttpClient client, PatientDto patient, PayerDto payer, decimal amount, DateOnly dueDate)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, DueDate = dueDate, Lines = [BillingApi.FreeTextLine("Treatment", 1m, amount, 0m)] });
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    // A payment dated today in India by the clock (no date sent), with the reference BR-301 asks for all but cash.
    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, PaymentMethod method, decimal amount)
    {
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/payments", UriKind.Relative),
            new RecordPaymentRequest { PayerId = payer.Id, Method = method, Amount = amount, Reference = method == PaymentMethod.Cash ? null : "REF-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PaymentAllocationsDto> AllocatedAsync(HttpClient client, PaymentDto payment, Guid billId, decimal amount)
    {
        using var response = await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = billId, Amount = amount }] },
            PayersApi.ETag(payment.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task<PaymentDto> CurrentAsync(HttpClient client, Guid paymentId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/payments/{paymentId}", UriKind.Relative));

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PaymentAllocationsDto> ReversalRequestedAsync(HttpClient maker, Guid paymentId)
    {
        var payment = await CurrentAsync(maker, paymentId);
        using var asked = await maker.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"/api/v1/payments/{paymentId}/reverse", UriKind.Relative), new RequestReversalRequest { Reason = "Asked for by the payer" }, PayersApi.ETag(payment.Version));

        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());

        return await asked.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task<PaymentAllocationsDto> ReversalApprovedAsync(HttpClient checker, Guid paymentId)
    {
        var payment = await CurrentAsync(checker, paymentId);
        using var approved = await checker.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"/api/v1/payments/{paymentId}/approve-reversal", UriKind.Relative), body: null, PayersApi.ETag(payment.Version));

        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        return await approved.ReadAsAsync<PaymentAllocationsDto>();
    }

    // What the scenario made, and the Viewer that reads the dashboard. It owns the host and that client.
    private sealed class Scenario(DatabaseApiFactory factory, FixedIdentity maker, HttpClient viewer, PayerDto first, PayerDto second) : IDisposable
    {
        public DatabaseApiFactory Factory { get; } = factory;

        public Guid TenantId { get; } = maker.TenantId!.Value.Value;

        public PayerDto First { get; } = first;

        public PayerDto Second { get; } = second;

        public Task<T> ReadAsync<T>(string pathAndQuery)
            where T : class
            => DashboardApiTests.ReadAsync<T>(viewer, pathAndQuery);

        public void Dispose()
        {
            viewer.Dispose();
            Factory.Dispose();
        }
    }
}
