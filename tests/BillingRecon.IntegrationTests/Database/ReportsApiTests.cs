using System.Net;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The reports of R1 (BR-901; US-40, US-42; ADR 0036) after a month of one hospital, in the manner of the Phase 4
/// scenario: bills of two payers, one dated the day before; payments by four methods, one dated the day before, allocated,
/// one allocation reversed, one payment reversed and one waiting for its reversal; a credit note applied in part; an
/// adjustment; a refund; a credit note waiting for a checker; and another hospital beside it. Every figure is held against
/// the ledger, summed here in SQL, never against the report's own sources; and the reconciliation finds no drift.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ReportsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // 17 October 2026 in India, the clock's day: fiscal year 2026-27, the month of October.
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly MonthStart = new(2026, 10, 1);
    private static readonly DateOnly MonthEnd = new(2026, 10, 31);

    [Fact]
    public async Task BR901_the_bill_register_lists_the_posted_bills_of_the_month_and_its_totals_equal_the_ledger()
    {
        await using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;

        var register = await scenario.ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>("bill-register");

        register.Period.ShouldBe(new ReportPeriodDto(MonthStart, MonthEnd));
        register.Items.Select(bill => (bill.BillId, bill.Status, bill.Total, bill.BalanceDue)).ShouldBe(
        [
            (scenario.YesterdayBill.Id, ReportBillStatus.Posted, 200.00m, 200.00m),
            (scenario.Admission.Id, ReportBillStatus.PartiallyPaid, 716.00m, 226.00m),
            (scenario.Dressing.Id, ReportBillStatus.Posted, 126.00m, 126.00m),
            (scenario.Consultation.Id, ReportBillStatus.Settled, 626.00m, 0.00m),
        ]);
        register.Items.Select(bill => bill.Number).ShouldBe(["BIL/26-27/000004", "BIL/26-27/000001", "BIL/26-27/000002", "BIL/26-27/000003"]);
        register.Items[1].PatientName.ShouldBe(scenario.FirstPatient.FullName);
        register.Items[1].PayerName.ShouldBe(scenario.First.Name);
        (register.TotalCount, register.Totals).ShouldBe((4, new BillRegisterTotalsDto(4, 1668.00m, 552.00m)));

        // The totals are the ledger's: what the bills came to is what their postings put on Receivable, and what is owed
        // on them is what is left there.
        register.Totals.Total.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND p.[Kind] = 'BillPosted' AND e.[Account] = 'Receivable' AND p.[BusinessDate] BETWEEN {MonthStart} AND {MonthEnd}"));
        register.Totals.BalanceDue.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[Account] = 'Receivable'"));
        foreach (var bill in register.Items)
        {
            bill.BalanceDue.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[Account] = 'Receivable' AND e.[SubjectId] = {bill.BillId}"), bill.Number);
        }

        // The filters combine; a page carries the totals of every page; a draft is in no register; one day is a period.
        var first = await scenario.ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>($"bill-register?payerId={scenario.First.Id}");
        first.Items.Select(bill => bill.BillId).ShouldBe([scenario.Admission.Id, scenario.Dressing.Id]);
        first.Totals.ShouldBe(new BillRegisterTotalsDto(2, 842.00m, 352.00m));
        (await scenario.ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>("bill-register?status=Posted")).Items.Select(bill => bill.BillId)
            .ShouldBe([scenario.YesterdayBill.Id, scenario.Dressing.Id]);
        var page = await scenario.ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>("bill-register?page=2&pageSize=3");
        (page.Page, page.PageSize, page.TotalCount, page.Items.Count, page.Totals).ShouldBe((2, 3, 4, 1, register.Totals));
        page.Items.ShouldHaveSingleItem().BillId.ShouldBe(scenario.Consultation.Id);
        var oneDay = await scenario.ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>($"bill-register?from={Yesterday:yyyy-MM-dd}&to={Yesterday:yyyy-MM-dd}");
        oneDay.Items.ShouldHaveSingleItem().BillId.ShouldBe(scenario.YesterdayBill.Id);
        register.Items.ShouldNotContain(bill => bill.BillId == scenario.Draft.Id);
    }

    [Fact]
    public async Task BR901_the_payment_register_lists_the_payments_of_the_month_reversed_ones_included_and_its_totals_equal_the_ledger()
    {
        await using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;

        var register = await scenario.ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>("payment-register");

        register.Items.Select(payment => (payment.PaymentId, payment.Method, payment.Amount, payment.UnappliedAmount, payment.Status)).ShouldBe(
        [
            (scenario.Upi.Id, ReportPaymentMethod.UPI, 300.00m, 300.00m, ReportPaymentStatus.Posted),
            (scenario.Cheque.Id, ReportPaymentMethod.Cheque, 500.00m, 50.00m, ReportPaymentStatus.Posted),
            (scenario.Cash.Id, ReportPaymentMethod.Cash, 600.00m, 0.00m, ReportPaymentStatus.Posted),
            (scenario.Card.Id, ReportPaymentMethod.Card, 150.00m, 0.00m, ReportPaymentStatus.Reversed),
            (scenario.Waiting.Id, ReportPaymentMethod.Cash, 200.00m, 200.00m, ReportPaymentStatus.PendingReversal),
        ]);
        register.Items.Single(payment => payment.PaymentId == scenario.Card.Id).ReversedAtUtc.ShouldNotBeNull();
        register.Totals.ShouldBe(new PaymentRegisterTotalsDto(5, 1750.00m, 550.00m));

        // What the payments brought in is what their postings put on Cash; what they hold is the UnappliedCash account.
        register.Totals.Amount.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND p.[Kind] = 'PaymentRecorded' AND e.[Account] = 'Cash' AND p.[BusinessDate] BETWEEN {MonthStart} AND {MonthEnd}"));
        register.Totals.UnappliedAmount.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[Account] = 'UnappliedCash'"));

        (await scenario.ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>("payment-register?method=Cash")).Totals.ShouldBe(new PaymentRegisterTotalsDto(2, 800.00m, 200.00m));
        (await scenario.ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>("payment-register?status=Reversed")).Items.ShouldHaveSingleItem().PaymentId.ShouldBe(scenario.Card.Id);
        (await scenario.ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>($"payment-register?payerId={scenario.First.Id}")).Items.ShouldHaveSingleItem().PaymentId.ShouldBe(scenario.Cheque.Id);
    }

    [Fact]
    public async Task US42_the_collection_of_a_day_by_method_ties_to_the_cash_entries_of_that_business_date()
    {
        await using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;

        var today = await scenario.ReadAsync<DailyCollectionDto>("daily-collection");
        var yesterday = await scenario.ReadAsync<DailyCollectionDto>($"daily-collection?date={Yesterday:yyyy-MM-dd}");

        today.Date.ShouldBe(Today);
        today.Methods.Select(method => method.Method).ShouldBe(Enum.GetValues<ReportPaymentMethod>());
        today.Methods.Select(method => (method.Method, method.Payments, method.Received, method.Reversed, method.Refunded, method.Net)).ShouldBe(
        [
            (ReportPaymentMethod.Cash, 2, 800.00m, 0.00m, 0.00m, 800.00m),
            (ReportPaymentMethod.Card, 1, 150.00m, 150.00m, 0.00m, 0.00m),
            (ReportPaymentMethod.UPI, 0, 0.00m, 0.00m, 0.00m, 0.00m),
            (ReportPaymentMethod.BankTransfer, 0, 0.00m, 0.00m, 50.00m, -50.00m),
            (ReportPaymentMethod.Cheque, 1, 500.00m, 0.00m, 0.00m, 500.00m),
        ]);
        today.Totals.ShouldBe(new CollectionTotalsDto(4, 1450.00m, 150.00m, 50.00m, 1250.00m));

        // The payment dated the day before is in that day's collection, not today's (BR-003).
        yesterday.Methods.Single(method => method.Method == ReportPaymentMethod.UPI).ShouldBe(new CollectionByMethodDto(ReportPaymentMethod.UPI, 1, 300.00m, 0.00m, 0.00m, 300.00m));
        yesterday.Totals.ShouldBe(new CollectionTotalsDto(1, 300.00m, 0.00m, 0.00m, 300.00m));

        foreach (var (date, collection) in new[] { (Today, today), (Yesterday, yesterday) })
        {
            collection.Totals.Net.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND e.[Account] = 'Cash' AND p.[BusinessDate] = {date}"));
            collection.Totals.Received.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND e.[Account] = 'Cash' AND p.[Kind] = 'PaymentRecorded' AND p.[BusinessDate] = {date}"));
            collection.Totals.Reversed.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND e.[Account] = 'Cash' AND p.[Kind] = 'PaymentReversed' AND p.[BusinessDate] = {date}"));
            collection.Totals.Refunded.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit]), 0) AS [Value] FROM dbo.LedgerPostings p JOIN dbo.LedgerEntries e ON e.[PostingId] = p.[Id] WHERE p.[TenantId] = {tenant} AND e.[Account] = 'Cash' AND p.[Kind] = 'RefundPosted' AND p.[BusinessDate] = {date}"));
        }
    }

    [Fact]
    public async Task US40_what_payers_owe_and_what_is_held_for_them_equals_their_ledger_sums()
    {
        await using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;

        var outstanding = await scenario.ReadAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>("outstanding-by-payer");
        var first = await scenario.ReadAsync<PayerOutstandingDto>($"payers/{scenario.First.Id}/balance");
        var idle = await scenario.ReadAsync<PayerOutstandingDto>($"payers/{scenario.Idle.Id}/balance");

        // The largest net outstanding first; a payer with nothing owed or held is not listed, though it can be asked for.
        outstanding.Items.Select(payer => (payer.PayerId, payer.Receivable, payer.UnappliedCash, payer.CreditAvailable, payer.NetOutstanding)).ShouldBe(
        [
            (scenario.First.Id, 352.00m, 50.00m, 0.00m, 302.00m),
            (scenario.Second.Id, 200.00m, 500.00m, 100.00m, -400.00m),
        ]);
        outstanding.Totals.ShouldBe(new OutstandingTotalsDto(2, 552.00m, 550.00m, 100.00m, -98.00m));
        first.ShouldBe(outstanding.Items[0]);
        idle.ShouldBe(new PayerOutstandingDto(scenario.Idle.Id, scenario.Idle.Name, 0m, 0m, 0m, 0m));

        foreach (var payer in outstanding.Items)
        {
            payer.Receivable.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[PayerId] = {payer.PayerId} AND e.[Account] = 'Receivable'"));
            payer.UnappliedCash.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[PayerId] = {payer.PayerId} AND e.[Account] = 'UnappliedCash'"));
            payer.CreditAvailable.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[PayerId] = {payer.PayerId} AND e.[Account] = 'CreditAvailable'"));
        }

        (await scenario.ReadAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>($"outstanding-by-payer?search={Uri.EscapeDataString(scenario.Second.Name[..5])}")).Items
            .ShouldContain(payer => payer.PayerId == scenario.Second.Id);

        // Another hospital's payer, or one nobody has, is not found.
        using var otherTenants = await scenario.Viewer.GetAsync(new Uri($"/api/v1/reports/payers/{scenario.OtherPayer.Id}/balance", UriKind.Relative));
        using var nobodys = await scenario.Viewer.GetAsync(new Uri($"/api/v1/reports/payers/{Guid.NewGuid()}/balance", UriKind.Relative));
        await otherTenants.ShouldBeProblemAsync(HttpStatusCode.NotFound, "payer_not_found");
        await nobodys.ShouldBeProblemAsync(HttpStatusCode.NotFound, "payer_not_found");
    }

    [Fact]
    public async Task BR901_unapplied_cash_and_available_credit_list_what_each_document_holds_and_equal_their_ledger_accounts()
    {
        await using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;

        var unapplied = await scenario.ReadAsync<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>>("unapplied-cash");
        var credit = await scenario.ReadAsync<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>>("available-credit");

        unapplied.Items.Select(payment => (payment.PaymentId, payment.UnappliedAmount, payment.Status)).ShouldBe(
        [
            (scenario.Upi.Id, 300.00m, ReportPaymentStatus.Posted),
            (scenario.Cheque.Id, 50.00m, ReportPaymentStatus.Posted),
            (scenario.Waiting.Id, 200.00m, ReportPaymentStatus.PendingReversal),
        ]);
        unapplied.Totals.ShouldBe(new UnappliedCashTotalsDto(3, 550.00m));
        unapplied.Totals.UnappliedAmount.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[Account] = 'UnappliedCash'"));
        foreach (var payment in unapplied.Items)
        {
            payment.UnappliedAmount.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[Account] = 'UnappliedCash' AND e.[SubjectId] = {payment.PaymentId}"), payment.Number);
        }

        var note = credit.Items.ShouldHaveSingleItem();
        (note.CreditNoteId, note.BillId, note.BillNumber, note.Total, note.AvailableAmount).ShouldBe((scenario.Credit.Id, scenario.Consultation.Id, "BIL/26-27/000003", 126.00m, 100.00m));
        credit.Totals.ShouldBe(new AvailableCreditTotalsDto(1, 100.00m));
        credit.Totals.AvailableAmount.ShouldBe(await LedgerAsync($"SELECT ISNULL(SUM(e.[Credit] - e.[Debit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = {tenant} AND e.[Account] = 'CreditAvailable'"));

        (await scenario.ReadAsync<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>>($"unapplied-cash?payerId={scenario.First.Id}")).Items.ShouldHaveSingleItem().PaymentId.ShouldBe(scenario.Cheque.Id);
        (await scenario.ReadAsync<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>>($"available-credit?payerId={scenario.First.Id}")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task BR901_the_pending_approvals_are_the_requests_that_wait_with_their_documents_none_of_them_posted_yet()
    {
        await using var scenario = await ScenarioAsync();
        var tenant = scenario.TenantId;
        scenario.Factory.Clock.Advance(TimeSpan.FromDays(2));

        var pending = await scenario.ReadAsync<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>("pending-approvals");

        pending.Items.Select(request => (request.DocumentType, request.Action, request.DocumentId, request.PayerId, request.Amount, request.WaitingDays)).ShouldBe(
        [
            (ApprovalDocumentType.Payment, ApprovalAction.Reverse, scenario.Waiting.Id, (Guid?)scenario.Second.Id, (decimal?)200.00m, 2),
            (ApprovalDocumentType.CreditNote, ApprovalAction.Post, scenario.PendingCredit.Id, (Guid?)scenario.First.Id, (decimal?)126.00m, 2),
        ]);
        pending.Items.Select(request => request.PayerName).ShouldBe([scenario.Second.Name, scenario.First.Name]);
        pending.Items.ShouldAllBe(request => request.MakerUserId == scenario.Maker.UserId!.Value.Value);
        pending.Totals.ShouldBe(new PendingApprovalsTotalsDto(2, pending.Items[0].RequestedAtUtc));

        // Neither document has the posting its request waits for.
        (await LedgerAsync($"SELECT CAST(COUNT(*) AS decimal(18,2)) AS [Value] FROM dbo.LedgerPostings p WHERE p.[TenantId] = {tenant} AND p.[Kind] = 'PaymentReversed' AND p.[DocumentId] = {scenario.Waiting.Id}")).ShouldBe(0m);
        (await LedgerAsync($"SELECT CAST(COUNT(*) AS decimal(18,2)) AS [Value] FROM dbo.LedgerPostings p WHERE p.[TenantId] = {tenant} AND p.[DocumentId] = {scenario.PendingCredit.Id}")).ShouldBe(0m);

        (await scenario.ReadAsync<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>("pending-approvals?documentType=CreditNote")).Items.ShouldHaveSingleItem().DocumentId.ShouldBe(scenario.PendingCredit.Id);
    }

    [Fact]
    public async Task BR002_another_hospital_s_documents_are_in_no_report_and_a_new_hospital_s_reports_are_empty()
    {
        await using var scenario = await ScenarioAsync();
        var empty = await NewApiTenantAsync();
        using var emptyViewer = scenario.Factory.CreateClientFor(empty, "Viewer");

        var bills = await scenario.ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>("bill-register");
        var payments = await scenario.ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>("payment-register");
        var outstanding = await scenario.ReadAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>("outstanding-by-payer");
        bills.Items.ShouldNotContain(bill => bill.BillId == scenario.OtherBill.Id);
        payments.Items.ShouldNotContain(payment => payment.PaymentId == scenario.OtherPayment.Id);
        outstanding.Items.ShouldNotContain(payer => payer.PayerId == scenario.OtherPayer.Id);

        (await ReadAsync<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>(emptyViewer, "bill-register")).Totals.ShouldBe(new BillRegisterTotalsDto(0, 0m, 0m));
        (await ReadAsync<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>(emptyViewer, "payment-register")).Totals.ShouldBe(new PaymentRegisterTotalsDto(0, 0m, 0m));
        (await ReadAsync<DailyCollectionDto>(emptyViewer, "daily-collection")).Totals.ShouldBe(new CollectionTotalsDto(0, 0m, 0m, 0m, 0m));
        (await ReadAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>(emptyViewer, "outstanding-by-payer")).Items.ShouldBeEmpty();
        (await ReadAsync<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>>(emptyViewer, "unapplied-cash")).Totals.ShouldBe(new UnappliedCashTotalsDto(0, 0m));
        (await ReadAsync<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>>(emptyViewer, "available-credit")).Totals.ShouldBe(new AvailableCreditTotalsDto(0, 0m));
        (await ReadAsync<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>(emptyViewer, "pending-approvals")).Totals.ShouldBe(new PendingApprovalsTotalsDto(0, null));

        await using var context = ApiContext(scenario.Maker);
        (await new ReconciliationQuery(context, scenario.Maker).FindDriftAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_report_refuses_a_period_it_cannot_cover_as_any_field_is_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var viewer = factory.CreateClientFor(tenant, "Viewer");

        using var half = await viewer.GetAsync(Report("bill-register?from=2026-10-01"));
        using var backwards = await viewer.GetAsync(Report("payment-register?from=2026-10-31&to=2026-10-01"));
        using var tooLong = await viewer.GetAsync(Report("bill-register?from=2025-10-01&to=2026-10-31"));
        using var method = await viewer.GetAsync(Report("payment-register?method=Barter"));

        foreach (var response in new[] { half, backwards, tooLong })
        {
            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
            problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["to"]);
        }

        await method.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
    }

    private static Uri Report(string pathAndQuery) => new($"/api/v1/reports/{pathAndQuery}", UriKind.Relative);

    private static async Task<T> ReadAsync<T>(HttpClient client, string pathAndQuery)
        where T : class
    {
        using var response = await client.GetAsync(Report(pathAndQuery));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    // A sum of the ledger, as the application's login reads it, in SQL of the test's own.
    private async Task<decimal> LedgerAsync(FormattableString sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQuery<decimal>(sql).SingleAsync();
    }

    // The month of the hospital, built through the API as its users would: a clerk who also takes money, and a supervisor
    // who approves. The reports are read by a Viewer.
    private async Task<Scenario> ScenarioAsync()
    {
        var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        var checker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        var viewerIdentity = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var viewer = factory.CreateClientFor(viewerIdentity, "Viewer");

        var (firstPatient, first) = await clerk.NewBillPartiesAsync();
        var (secondPatient, second) = await clerk.NewBillPartiesAsync();
        var (_, idle) = await clerk.NewBillPartiesAsync();

        // Four bills: two of the first payer, the second payer's consultation, and its follow-up of the day before.
        var admission = await PostedBillAsync(clerk, firstPatient, first, Today, BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m), BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var dressing = await PostedBillAsync(clerk, firstPatient, first, Today, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var consultation = await PostedBillAsync(clerk, secondPatient, second, Today, BillingApi.FreeTextLine("Specialist consultation", 1m, 500m, 0m), BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var yesterdayBill = await PostedBillAsync(clerk, secondPatient, second, Yesterday, BillingApi.FreeTextLine("Follow-up", 1m, 200m, 0m));
        var draft = await clerk.CreateBillAsync(new CreateBillRequest { PatientId = firstPatient.Id, PayerId = first.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = [BillingApi.FreeTextLine("Not yet posted", 1m, 999m, 0m)] });

        // The first payer pays 500.00 by cheque: 400.00 to the admission, 100.00 to the dressing bill.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var cheque = await RecordedAsync(clerk, first, Today, PaymentMethod.Cheque, 500m);
        var allocated = await AllocatedAsync(clerk, cheque, (admission.Id, 400m), (dressing.Id, 100m));

        // The second payer pays 600.00 in cash on the consultation, which then owes 26.00.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var cash = await RecordedAsync(clerk, second, Today, PaymentMethod.Cash, 600m);
        await AllocatedAsync(clerk, cash, (consultation.Id, 600m));

        // A credit note for the consultation's dressing: 26.00 of its 126.00 settles the consultation, 100.00 is left.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var credit = await ApprovedAsync(clerk, supervisor, CreditNote, await DraftedCreditNoteAsync(clerk, consultation));

        // A discount of 90.00 on the admission.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using (var drafted = await clerk.PostJsonAsync(new Uri("/api/v1/adjustments", UriKind.Relative), new CreateAdjustmentRequest { BillId = admission.Id, Kind = AdjustmentKind.Discount, Amount = 90m, Reason = "Staff discount" }))
        {
            drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
            await ApprovedAsync(clerk, supervisor, Adjustment, await drafted.ReadAsAsync<AdjustmentDto>());
        }

        // The 100.00 on the dressing bill was the wrong bill: reversed; then 50.00 of it is refunded by bank transfer.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var wrong = allocated.Allocations.Single(allocation => allocation.BillId == dressing.Id);
        await ReversalApprovedAsync(clerk, supervisor, $"/api/v1/payments/{cheque.Id}/allocations/{wrong.Id}", cheque.Id);
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using (var asked = await clerk.PostJsonAsync(new Uri("/api/v1/refunds", UriKind.Relative), new CreateRefundRequest { PaymentId = cheque.Id, Amount = 50m, Method = PaymentMethod.BankTransfer, Reference = "UTR2026101700042", Reason = "Part of the wrong payment back" }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Created, await asked.Content.ReadAsStringAsync());
            await ApprovedAsync(clerk, supervisor, Refund, await asked.ReadAsAsync<RefundDto>());
        }

        // The second payer: 300.00 by UPI the day before, held; 150.00 by card, reversed; 200.00 in cash, its reversal waiting.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var upi = await RecordedAsync(clerk, second, Yesterday, PaymentMethod.UPI, 300m);
        var card = await RecordedAsync(clerk, second, Today, PaymentMethod.Card, 150m);
        await ReversalApprovedAsync(clerk, supervisor, $"/api/v1/payments/{card.Id}", card.Id);
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await RecordedAsync(clerk, second, Today, PaymentMethod.Cash, 200m);
        await ReversalRequestedAsync(clerk, waiting.Id);

        // A credit note of the first payer's dressing bill, waiting for a checker.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var pendingCredit = await SubmittedAsync(clerk, await DraftedCreditNoteAsync(clerk, dressing));

        // Another hospital, with a bill and a payment of its own.
        var other = await NewApiTenantAsync();
        using var otherClerk = factory.CreateClientFor(other, "BillingClerk", "Cashier");
        var (otherPatient, otherPayer) = await otherClerk.NewBillPartiesAsync();
        var otherBill = await PostedBillAsync(otherClerk, otherPatient, otherPayer, Today, BillingApi.FreeTextLine("Consultation", 1m, 400m, 0m));
        var otherPayment = await RecordedAsync(otherClerk, otherPayer, Today, PaymentMethod.Cash, 400m);

        clerk.Dispose();
        supervisor.Dispose();

        return new Scenario(factory, maker, viewer)
        {
            FirstPatient = firstPatient,
            First = first,
            Second = second,
            Idle = idle,
            Admission = admission,
            Dressing = dressing,
            Consultation = consultation,
            YesterdayBill = yesterdayBill,
            Draft = draft,
            Cheque = cheque,
            Cash = cash,
            Upi = upi,
            Card = card,
            Waiting = waiting,
            Credit = credit,
            PendingCredit = pendingCredit,
            OtherBill = otherBill,
            OtherPayer = otherPayer,
            OtherPayment = otherPayment,
        };
    }

    private static Uri CreditNote(Guid id, string action) => new($"/api/v1/credit-notes/{id}/{action}", UriKind.Relative);

    private static Uri Adjustment(Guid id, string action) => new($"/api/v1/adjustments/{id}/{action}", UriKind.Relative);

    private static Uri Refund(Guid id, string action) => new($"/api/v1/refunds/{id}/{action}", UriKind.Relative);

    private static async Task<BillDto> PostedBillAsync(HttpClient client, PatientDto patient, PayerDto payer, DateOnly billDate, params BillLineRequest[] lines)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = billDate, DueDate = billDate.AddDays(15), Lines = lines });
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, DateOnly paymentDate, PaymentMethod method, decimal amount)
    {
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/payments", UriKind.Relative),
            new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = paymentDate, Method = method, Reference = "REF-1", Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PaymentAllocationsDto> AllocatedAsync(HttpClient client, PaymentDto payment, params (Guid BillId, decimal Amount)[] lines)
    {
        using var response = await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [.. lines.Select(line => new AllocationRequest { BillId = line.BillId, Amount = line.Amount })] },
            PayersApi.ETag(payment.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task<PaymentDto> PaymentAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/payments/{id}", UriKind.Relative));

        return await response.ReadAsAsync<PaymentDto>();
    }

    // The maker asks for the reversal of a payment or of one of its allocations; the answer carries the payment.
    private static async Task<PaymentAllocationsDto> ReversalRequestedAsync(HttpClient maker, Guid paymentId, string? document = null)
    {
        var current = await PaymentAsync(maker, paymentId);
        using var asked = await maker.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"{document ?? $"/api/v1/payments/{paymentId}"}/reverse", UriKind.Relative), new RequestReversalRequest { Reason = "Asked for by the payer" }, PayersApi.ETag(current.Version));

        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());

        return await asked.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task ReversalApprovedAsync(HttpClient maker, HttpClient checker, string document, Guid paymentId)
    {
        var waiting = await ReversalRequestedAsync(maker, paymentId, document);
        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"{document}/approve-reversal", UriKind.Relative), body: null, PayersApi.ETag(waiting.Payment.Version));

        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
    }

    // A draft that credits the whole dressing line of the bill.
    private static async Task<CreditNoteDto> DraftedCreditNoteAsync(HttpClient client, BillDto bill)
    {
        var line = bill.Lines.Single(candidate => candidate.Description == "Dressing");
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/credit-notes", UriKind.Relative),
            new CreateCreditNoteRequest { BillId = bill.Id, CreditNoteDate = Today, Reason = "Dressing not done", Lines = [new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity }] });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> SubmittedAsync(HttpClient maker, CreditNoteDto draft)
    {
        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());

        return await submitted.ReadAsAsync<CreditNoteDto>();
    }

    // A maker-checker document handed over by its maker and approved by the checker; the answer of the approval.
    private static async Task<T> ApprovedAsync<T>(HttpClient maker, HttpClient checker, Func<Guid, string, Uri> uri, T draft)
        where T : class
    {
        var (id, version) = IdAndVersion(draft);
        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, uri(id, "post"), body: null, PayersApi.ETag(version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var (_, waiting) = IdAndVersion(await submitted.ReadAsAsync<T>());

        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, uri(id, "approve"), body: null, PayersApi.ETag(waiting));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        return await approved.ReadAsAsync<T>();
    }

    private static (Guid Id, string Version) IdAndVersion<T>(T document) => document switch
    {
        CreditNoteDto creditNote => (creditNote.Id, creditNote.Version),
        AdjustmentDto adjustment => (adjustment.Id, adjustment.Version),
        RefundDto refund => (refund.Id, refund.Version),
        _ => throw new ArgumentException($"{typeof(T).Name} is not a maker-checker document of this scenario.", nameof(document)),
    };

    // What the scenario made, for the tests to look for in the reports. It owns the host and the Viewer's client.
    private sealed class Scenario(DatabaseApiFactory factory, FixedIdentity maker, HttpClient viewer) : IAsyncDisposable
    {
        public DatabaseApiFactory Factory { get; } = factory;

        public FixedIdentity Maker { get; } = maker;

        public Guid TenantId => Maker.TenantId!.Value.Value;

        public HttpClient Viewer { get; } = viewer;

        public required PatientDto FirstPatient { get; init; }

        public required PayerDto First { get; init; }

        public required PayerDto Second { get; init; }

        public required PayerDto Idle { get; init; }

        public required BillDto Admission { get; init; }

        public required BillDto Dressing { get; init; }

        public required BillDto Consultation { get; init; }

        public required BillDto YesterdayBill { get; init; }

        public required BillDto Draft { get; init; }

        public required PaymentDto Cheque { get; init; }

        public required PaymentDto Cash { get; init; }

        public required PaymentDto Upi { get; init; }

        public required PaymentDto Card { get; init; }

        public required PaymentDto Waiting { get; init; }

        public required CreditNoteDto Credit { get; init; }

        public required CreditNoteDto PendingCredit { get; init; }

        public required BillDto OtherBill { get; init; }

        public required PayerDto OtherPayer { get; init; }

        public required PaymentDto OtherPayment { get; init; }

        public Task<T> ReadAsync<T>(string pathAndQuery)
            where T : class
            => ReportsApiTests.ReadAsync<T>(Viewer, pathAndQuery);

        public async ValueTask DisposeAsync()
        {
            Viewer.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
