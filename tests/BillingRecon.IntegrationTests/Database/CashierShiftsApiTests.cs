using System.Net;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
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
/// The shift of the cash counter end to end, on the pipeline that ships and a real SQL Server (US-45 to US-48;
/// BR-310 to BR-315, BR-903, BR-603, BR-605, BR-804; ADR 0060): a cashier opens a shift, takes and pays out cash in
/// it, counts it and closes it; a difference waits for a second person; the day-end report of every shift equals the
/// daily collection. The expected cash is held against the ledger's own entries in SQL, and every scenario ends with
/// no drift (the base class sees to that).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CashierShiftsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);
    private static readonly Uri Refunds = new("/api/v1/refunds", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static int _references;

    [Fact]
    public async Task US45_US46_a_cashier_opens_a_shift_takes_and_pays_out_cash_and_closes_it_on_the_cash_the_ledger_has()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await supervisor.NewBillPartiesAsync();

        // Without a shift no cash is taken, and the refusal takes no number (BR-311, BR-801).
        using var early = await cashier.PostJsonAsync(Payments, Cash(payer, 300m));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftNotOpenCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Payment'")).ShouldBe(0);

        // The shift opens with its float, on today's business date; a second one is refused (BR-310).
        var shift = await cashier.OpenShiftAsync(500m);
        (shift.Status, shift.BusinessDate, shift.OpeningFloat, shift.ExpectedCash, shift.CashDocuments).ShouldBe((CashierShiftStatus.Open, Today, 500.00m, 500.00m, 0));
        shift.CashierUserId.ShouldBe(maker.UserId!.Value.Value);
        using var again = await cashier.PostJsonAsync(ShiftsApi.Shifts, new OpenCashierShiftRequest { OpeningFloat = 0m });
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftAlreadyOpenCode);

        // Cash in, sent without a date: it takes the date of the shift. Money by another method is no cash of the drawer.
        var cash = await CreatedAsync<PaymentDto>(cashier, Payments, Cash(payer, 300.50m) with { PaymentDate = null });
        cash.PaymentDate.ShouldBe(Today);
        var upi = await CreatedAsync<PaymentDto>(cashier, Payments, Upi(payer, 1000m));

        // Cash out: a refund the cashier drafted, paid out of this drawer when the supervisor approves it.
        await ApprovedRefundAsync(cashier, supervisor, new CreateRefundRequest { PaymentId = upi.Id, Amount = 100m, Method = PaymentMethod.Cash, Reason = "Paid twice" });

        // BR-312: the expected cash is the float and the Cash entries of the postings that name the shift.
        var counted = await ShiftAsync(cashier, shift.Id);
        (counted.CashReceived, counted.CashPaidOut, counted.ExpectedCash, counted.CashDocuments).ShouldBe((300.50m, 100.00m, 700.50m, 2));
        (await LedgerOfShiftAsync(shift.Id)).ShouldBe(200.50m);
        (await CountAsync($"SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [ShiftId] = '{shift.Id}'")).ShouldBe(2);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [ShiftId] IS NULL AND [Kind] = 'PaymentRecorded'")).ShouldBe(1);

        // Counted to the paisa: closed at once, with no second person (BR-313, BR-314).
        using var closing = await cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "close"), ShiftsApi.ExactCount(700.50m), PayersApi.ETag(counted.Version));
        closing.StatusCode.ShouldBe(HttpStatusCode.OK, await closing.Content.ReadAsStringAsync());
        var closed = await closing.ReadAsAsync<CashierShiftDto>();
        (closed.Status, closed.ExpectedCash, closed.CountedCash, closed.Difference, closed.DifferenceReason, closed.LooseChange).ShouldBe((CashierShiftStatus.Closed, 700.50m, 700.50m, 0.00m, null, 0.50m));
        closed.Count.ShouldNotBeNull().Where(line => line.Pieces > 0).Select(line => (line.Denomination, line.Pieces)).ShouldBe([(500, 1), (200, 1)]);
        closed.ClosedAtUtc.ShouldNotBeNull();
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CashierShift'")).ShouldBe(0);

        // BR-315: a closed shift takes no cash and is not closed again; the cashier opens a new one.
        using var late = await cashier.PostJsonAsync(Payments, Cash(payer, 10m));
        await late.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftNotOpenCode);
        using var twice = await cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "close"), ShiftsApi.ExactCount(700.50m), PayersApi.ETag(closed.Version));
        await twice.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftNotOpenCode);
        (await cashier.OpenShiftAsync()).Id.ShouldNotBe(shift.Id);

        // BR-701: the opening and the closing are audited; a cash document that joins the shift is not a change of it.
        (await TrailAsync(supervisor, shift.Id)).Select(record => record.Action).ShouldBe(["Opened", "Closed"], ignoreOrder: true);
    }

    [Fact]
    public async Task US47_a_closing_with_a_difference_waits_its_cashier_cannot_approve_it_and_a_second_person_decides()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();

        // The cashier is a supervisor too: the role would let them decide, the rule does not (BR-603).
        using var cashier = factory.CreateClientFor(maker, "Cashier", "Supervisor");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await supervisor.NewBillPartiesAsync();
        var shift = await cashier.OpenShiftAsync(1000m);
        await CreatedAsync<PaymentDto>(cashier, Payments, Cash(payer, 250m));
        var open = await ShiftAsync(cashier, shift.Id);
        var postings = await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings");

        // 1200.00 counted against 1250.00 expected: a shortage of 50.00, which needs its reason (BR-313).
        using var silent = await cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "close"), ShiftsApi.ExactCount(1200m), PayersApi.ETag(open.Version));
        await silent.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftDifferenceNeedsReasonCode);
        using var counted = await cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "close"), ShiftsApi.ExactCount(1200m, "A note of 50 is missing"), PayersApi.ETag(open.Version));
        counted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await counted.Content.ReadAsStringAsync());
        var waiting = await counted.ReadAsAsync<CashierShiftDto>();
        (waiting.Status, waiting.ExpectedCash, waiting.CountedCash, waiting.Difference, waiting.DifferenceReason, waiting.ClosedAtUtc)
            .ShouldBe((CashierShiftStatus.PendingApproval, 1250.00m, 1200.00m, -50.00m, "A note of 50 is missing", null));

        // The request is in the checker's list, made by the cashier (BR-605).
        var pending = (await ReadAsync<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>>(supervisor, "/api/v1/reports/pending-approvals")).Items.ShouldHaveSingleItem();
        (pending.DocumentType, pending.Action, pending.DocumentId, pending.MakerUserId, pending.MakerComment)
            .ShouldBe((ApprovalDocumentType.CashierShift, ApprovalAction.Close, shift.Id, maker.UserId!.Value.Value, "A note of 50 is missing"));

        // While it waits: no cash joins it, no other shift is opened, and its cashier decides nothing.
        using var cashWhileWaiting = await cashier.PostJsonAsync(Payments, Cash(payer, 10m));
        await cashWhileWaiting.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftNotOpenCode);
        using var another = await cashier.PostJsonAsync(ShiftsApi.Shifts, new OpenCashierShiftRequest { OpeningFloat = 0m });
        await another.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftAlreadyOpenCode);
        using var own = await cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        using var ownRejection = await cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "reject"), new DecideCashierShiftRequest { Comment = "No" }, PayersApi.ETag(waiting.Version));
        await ownRejection.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);

        // A rejection says why, and the shift is open again without its count (BR-605).
        using var wordless = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "reject"), new DecideCashierShiftRequest(), PayersApi.ETag(waiting.Version));
        await wordless.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        using var rejection = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "reject"), new DecideCashierShiftRequest { Comment = "Count it again" }, PayersApi.ETag(waiting.Version));
        rejection.StatusCode.ShouldBe(HttpStatusCode.OK, await rejection.Content.ReadAsStringAsync());
        var reopened = await rejection.ReadAsAsync<CashierShiftDto>();
        (reopened.Status, reopened.Count, reopened.CountedCash, reopened.Difference, reopened.DifferenceReason, reopened.ExpectedCash)
            .ShouldBe((CashierShiftStatus.Open, null, null, null, null, 1250.00m));

        // Counted again, still short: it waits again, and the second person approves. That closes it (BR-314, BR-606).
        var recounted = await cashier.CloseShiftAsync(reopened, ShiftsApi.ExactCount(1210m, "40 short after the recount"));
        recounted.Status.ShouldBe(CashierShiftStatus.PendingApproval);
        using var approval = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "approve"), new DecideCashierShiftRequest { Comment = "Seen" }, PayersApi.ETag(recounted.Version));
        approval.StatusCode.ShouldBe(HttpStatusCode.OK, await approval.Content.ReadAsStringAsync());
        var closed = await approval.ReadAsAsync<CashierShiftDto>();
        (closed.Status, closed.Difference, closed.DifferenceReason).ShouldBe((CashierShiftStatus.Closed, -40.00m, "40 short after the recount"));
        closed.ClosedAtUtc.ShouldNotBeNull();

        // A decision is final, and the difference is recorded, never posted: the ledger has no posting more (BR-314).
        using var late = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "approve"), body: null, PayersApi.ETag(closed.Version));
        await late.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftNotPendingApprovalCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(postings);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CashierShift' AND [Action] = 'Close' AND [Status] <> 'Pending'")).ShouldBe(2);

        // BR-701: every step is in the trail of the shift, the reasons with them.
        var trail = await TrailAsync(supervisor, shift.Id);
        trail.Select(record => record.Action).ShouldBe(["Opened", "SubmittedForApproval", "ClosingRejected", "SubmittedForApproval", "ClosingApproved"], ignoreOrder: true);
        trail.Where(record => record.Action == "ClosingRejected").ShouldHaveSingleItem().Reason.ShouldBe("Count it again");
    }

    [Fact]
    public async Task BR311_cash_in_a_shift_is_dated_the_business_date_of_the_shift_also_after_midnight()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await supervisor.NewBillPartiesAsync();
        var shift = await cashier.OpenShiftAsync();

        // The shift goes on past midnight in India: it keeps the date of its opening.
        factory.Clock.Advance(TimeSpan.FromDays(1));
        var undated = await CreatedAsync<PaymentDto>(cashier, Payments, Cash(payer, 40m) with { PaymentDate = null });
        undated.PaymentDate.ShouldBe(Today);
        using var datedToday = await cashier.PostJsonAsync(Payments, Cash(payer, 40m) with { PaymentDate = Today.AddDays(1) });
        var problem = await datedToday.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftDateMismatchCode);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("2026-10-17");

        // The refusal took no number, and money by another method is dated as before.
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Payment' AND [NextNumber] = 2")).ShouldBe(1);
        (await CreatedAsync<PaymentDto>(cashier, Payments, Upi(payer, 5m) with { PaymentDate = null })).PaymentDate.ShouldBe(Today.AddDays(1));

        // A cash refund dated another day than the shift of its drafter is refused at its approval, and waits.
        var upi = await CreatedAsync<PaymentDto>(cashier, Payments, Upi(payer, 500m) with { PaymentDate = Today });
        var draft = await CreatedAsync<RefundDto>(cashier, Refunds, new CreateRefundRequest { PaymentId = upi.Id, RefundDate = Today.AddDays(1), Amount = 20m, Method = PaymentMethod.Cash, Reason = "Paid twice" });
        using var submitted = await cashier.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/refunds/{draft.Id}/post", UriKind.Relative), body: null, PayersApi.ETag(draft.Version));
        var waiting = await submitted.ReadAsAsync<RefundDto>();
        using var otherDay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/refunds/{draft.Id}/approve", UriKind.Relative), body: null, PayersApi.ETag(waiting.Version));
        await otherDay.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftDateMismatchCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Refunds WHERE [Status] = 'PendingApproval' AND [Number] IS NULL")).ShouldBe(1);

        // Every posting that names the shift carries its business date.
        (await CountAsync($"SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [ShiftId] = '{shift.Id}' AND [BusinessDate] <> '2026-10-17'")).ShouldBe(0);
    }

    [Fact]
    public async Task BR903_the_day_end_report_of_every_shift_equals_the_daily_collection()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        var second = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var clerk = factory.CreateClientFor(second, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var viewer = factory.CreateClientFor(new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())), "Viewer");
        var (_, payer) = await supervisor.NewBillPartiesAsync();

        // Two drawers. The first: 300.00 and 200.00 in, a refund of 50.00 out, and the 200.00 reversed while it is open.
        var first = await cashier.OpenShiftAsync(1000m);
        await CreatedAsync<PaymentDto>(cashier, Payments, Cash(payer, 300m));
        var mistaken = await CreatedAsync<PaymentDto>(cashier, Payments, Cash(payer, 200m));
        var card = await CreatedAsync<PaymentDto>(cashier, Payments, Upi(payer, 400m) with { Method = PaymentMethod.Card });
        await ApprovedRefundAsync(cashier, supervisor, new CreateRefundRequest { PaymentId = card.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" });
        await ReversedAsync(cashier, supervisor, mistaken.Id);

        // The second, a clerk who takes cash: 150.00 in; it stays open.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var other = await clerk.OpenShiftAsync();
        var kept = await CreatedAsync<PaymentDto>(clerk, Payments, Cash(payer, 150m));

        // The first is closed with 5.00 too much, approved; then its 300.00 is reversed: its drawer is no longer
        // touched, and the reversal is cash outside the shifts.
        var counted = await cashier.CloseShiftAsync(await ShiftAsync(cashier, first.Id), ShiftsApi.ExactCount(1255m, "Five too much"));
        counted.Status.ShouldBe(CashierShiftStatus.PendingApproval);
        using var approval = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(first.Id, "approve"), body: null, PayersApi.ETag(counted.Version));
        approval.StatusCode.ShouldBe(HttpStatusCode.OK, await approval.Content.ReadAsStringAsync());
        var firstPayment = (await ReadAsync<PageOf<PaymentDto>>(cashier, "/api/v1/payments?method=Cash&pageSize=100")).Items.Single(payment => payment.Amount == 300m);
        await ReversedAsync(cashier, supervisor, firstPayment.Id);

        var report = await ReadAsync<DayEndDto>(viewer, "/api/v1/reports/day-end");
        report.Date.ShouldBe(Today);
        report.Shifts.Select(row => (row.ShiftId, row.Status, row.OpeningFloat, row.Received, row.PaidOut, row.Net, row.ExpectedCash, row.CountedCash, row.Difference)).ShouldBe(
        [
            (first.Id, ReportShiftStatus.Closed, 1000.00m, 500.00m, 250.00m, 250.00m, 1250.00m, (decimal?)1255.00m, (decimal?)5.00m),
            (other.Id, ReportShiftStatus.Open, 0.00m, 150.00m, 0.00m, 150.00m, 150.00m, (decimal?)null, (decimal?)null),
        ]);
        report.Shifts.Select(row => row.CashierName).ShouldAllBe(name => name == "Test user");
        report.ShiftsTotal.ShouldBe(new DayEndCashDto(650.00m, 250.00m, 400.00m));
        report.OutsideShifts.ShouldBe(new DayEndCashDto(0.00m, 300.00m, -300.00m));
        report.Cash.ShouldBe(new DayEndCashDto(650.00m, 550.00m, 100.00m));
        (report.Difference, report.UnclosedShifts).ShouldBe((5.00m, 1));

        // BR-903: its cash is the Cash row of the daily collection, and both are the ledger's Cash entries of cash documents.
        var collection = (await ReadAsync<DailyCollectionDto>(viewer, "/api/v1/reports/daily-collection")).Methods.Single(row => row.Method == ReportPaymentMethod.Cash);
        (report.Cash.Received, report.Cash.PaidOut, report.Cash.Net).ShouldBe((collection.Received, collection.Reversed + collection.Refunded, collection.Net));
        (await DecimalAsync(
            """
            SELECT COALESCE(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e JOIN dbo.LedgerPostings p ON p.[Id] = e.[PostingId]
            WHERE e.[Account] = 'Cash' AND p.[BusinessDate] = '2026-10-17'
              AND (EXISTS (SELECT 1 FROM dbo.Payments d WHERE d.[Id] = p.[DocumentId] AND d.[Method] = 'Cash')
                OR EXISTS (SELECT 1 FROM dbo.Refunds d WHERE d.[Id] = p.[DocumentId] AND d.[Method] = 'Cash'))
            """)).ShouldBe(report.Cash.Net);

        // The frozen figures of the closed shift are still what the ledger has for it.
        (await LedgerOfShiftAsync(first.Id)).ShouldBe(250.00m);
        (await ShiftAsync(supervisor, first.Id)).ExpectedCash.ShouldBe(1250.00m);
        kept.Status.ShouldBe(PaymentStatus.Posted);

        // Another day has no shift and no cash; the report is not a cashier's or a clerk's to read.
        var empty = await ReadAsync<DayEndDto>(supervisor, "/api/v1/reports/day-end?date=2026-10-16");
        (empty.Shifts.Count, empty.Cash, empty.UnclosedShifts).ShouldBe((0, new DayEndCashDto(0m, 0m, 0m), 0));
        using var notForCashier = await cashier.GetAsync(new Uri("/api/v1/reports/day-end", UriKind.Relative));
        notForCashier.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_cashier_reads_and_closes_only_their_own_shift_and_another_hospital_sees_none()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        var second = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        var elsewhere = await NewApiTenantAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var colleague = factory.CreateClientFor(second, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var outsider = factory.CreateClientFor(elsewhere, "TenantAdmin");
        var mine = await cashier.OpenShiftAsync(100m);
        var theirs = await colleague.OpenShiftAsync(200m);

        // A cashier reads the shifts they opened, whatever is asked; a supervisor reads all, or one cashier's.
        (await ListAsync(cashier, string.Empty)).Select(shift => shift.Id).ShouldBe([mine.Id]);
        (await ListAsync(cashier, $"cashierUserId={second.UserId!.Value.Value}")).Select(shift => shift.Id).ShouldBe([mine.Id]);
        (await ListAsync(supervisor, string.Empty)).Select(shift => shift.Id).ShouldBe([mine.Id, theirs.Id], ignoreOrder: true);
        (await ListAsync(supervisor, $"cashierUserId={second.UserId!.Value.Value}&unclosed=true&date=2026-10-17")).Select(shift => shift.Id).ShouldBe([theirs.Id]);
        (await ListAsync(supervisor, "mine=true")).ShouldBeEmpty();
        (await ListAsync(supervisor, "status=Closed")).ShouldBeEmpty();
        using var notMine = await cashier.GetAsync(ShiftsApi.Shift(theirs.Id));
        await notMine.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.ShiftNotFoundCode);
        (await ShiftAsync(supervisor, theirs.Id)).OpeningFloat.ShouldBe(200.00m);

        // A shift is closed by its own cashier, a supervisor included.
        using var byColleague = await colleague.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(mine.Id, "close"), ShiftsApi.ExactCount(100m), PayersApi.ETag(mine.Version));
        await byColleague.ShouldBeProblemAsync(HttpStatusCode.Forbidden, PaymentsErrors.ShiftNotOwnCode);
        using var bySupervisor = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(mine.Id, "close"), ShiftsApi.ExactCount(100m), PayersApi.ETag(mine.Version));
        await bySupervisor.ShouldBeProblemAsync(HttpStatusCode.Forbidden, PaymentsErrors.ShiftNotOwnCode);

        // The shape of a count and the version are checked before anything else is read.
        using var twice = await cashier.SendWithIfMatchAsync(
            HttpMethod.Post,
            ShiftsApi.Shift(mine.Id, "close"),
            new CloseCashierShiftRequest { Count = [new CashCountLineRequest { Denomination = 100, Pieces = 1 }, new CashCountLineRequest { Denomination = 100, Pieces = 1 }] },
            PayersApi.ETag(mine.Version));
        await twice.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        using var noNote = await cashier.SendWithIfMatchAsync(
            HttpMethod.Post, ShiftsApi.Shift(mine.Id, "close"), new CloseCashierShiftRequest { Count = [new CashCountLineRequest { Denomination = 25, Pieces = 4 }] }, PayersApi.ETag(mine.Version));
        await noNote.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        using var rupee = await cashier.SendWithIfMatchAsync(
            HttpMethod.Post, ShiftsApi.Shift(mine.Id, "close"), new CloseCashierShiftRequest { Count = [], LooseChange = 1m }, PayersApi.ETag(mine.Version));
        await rupee.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        using var unversioned = await cashier.PostJsonAsync(ShiftsApi.Shift(mine.Id, "close"), ShiftsApi.ExactCount(100m));
        unversioned.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        using var negative = await outsider.PostJsonAsync(ShiftsApi.Shifts, new OpenCashierShiftRequest { OpeningFloat = -1m });
        await negative.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);

        // BR-002: another hospital finds neither shift, by list or by id.
        (await ListAsync(outsider, string.Empty)).ShouldBeEmpty();
        using var across = await outsider.GetAsync(ShiftsApi.Shift(mine.Id));
        await across.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.ShiftNotFoundCode);
        using var closeAcross = await outsider.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(mine.Id, "close"), ShiftsApi.ExactCount(100m), PayersApi.ETag(mine.Version));
        await closeAcross.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.ShiftNotFoundCode);
        (await ReadAsync<DayEndDto>(outsider, "/api/v1/reports/day-end")).Shifts.ShouldBeEmpty();
    }

    [Fact]
    public async Task BR310_two_openings_by_one_user_at_once_give_exactly_one_shift()
    {
        var gate = new RaceGate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services => services.GateCommits(gate));
        var maker = await NewApiTenantAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");

        // Held at the gate: the second opening has found no shift and waits in front of its save while the first runs
        // from start to end. The unique index refuses it: the one answer of the rule, never a fault.
        var openings = await gate.OvertakeAsync(
            () => cashier.PostJsonAsync(ShiftsApi.Shifts, new OpenCashierShiftRequest { OpeningFloat = 100m }),
            () => cashier.PostJsonAsync(ShiftsApi.Shifts, new OpenCashierShiftRequest { OpeningFloat = 200m }));

        try
        {
            openings[0].StatusCode.ShouldBe(HttpStatusCode.Created, await openings[0].Content.ReadAsStringAsync());
            await openings[1].ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.ShiftAlreadyOpenCode);
        }
        finally
        {
            foreach (var response in openings)
            {
                response.Dispose();
            }
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CashierShifts WHERE [OpeningFloat] = 100")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CashierShifts")).ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BR804_a_closing_and_a_cash_payment_at_once_never_close_the_shift_on_a_figure_the_ledger_no_longer_has(bool paymentFirst)
    {
        var gate = new RaceGate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services => services.GateCommits(gate));
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await supervisor.NewBillPartiesAsync();
        var shift = await cashier.OpenShiftAsync(100m);

        Task<HttpResponseMessage> Close() => cashier.SendWithIfMatchAsync(HttpMethod.Post, ShiftsApi.Shift(shift.Id, "close"), ShiftsApi.ExactCount(100m), PayersApi.ETag(shift.Version));
        Task<HttpResponseMessage> Pay() => cashier.PostJsonAsync(Payments, Cash(payer, 60m));

        // Held at the gate: the loser has read the shift, and the closing the ledger too, and waits in front of its
        // save while the winner runs from start to end. The shift it read is stale when it saves: 409, whichever it is.
        var responses = paymentFirst ? await gate.OvertakeAsync(Pay, Close) : await gate.OvertakeAsync(Close, Pay);

        try
        {
            responses[0].IsSuccessStatusCode.ShouldBeTrue(await responses[0].Content.ReadAsStringAsync());
            await responses[1].ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var after = await ShiftAsync(cashier, shift.Id);
        if (paymentFirst)
        {
            // The payment is in, and the shift is open on the cash the ledger has now.
            (after.Status, after.ExpectedCash, after.CashDocuments).ShouldBe((CashierShiftStatus.Open, 160.00m, 1));
            (await LedgerOfShiftAsync(shift.Id)).ShouldBe(60.00m);
        }
        else
        {
            // The shift is closed on 100.00, and the payment left nothing: no row, no posting, no number (BR-801).
            (after.Status, after.ExpectedCash, after.CountedCash, after.Difference).ShouldBe((CashierShiftStatus.Closed, 100.00m, 100.00m, 0.00m));
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Payments")).ShouldBe(0);
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Payment' AND [NextNumber] > 1")).ShouldBe(0);
        }
    }

    private static RecordPaymentRequest Cash(PayerDto payer, decimal amount)
        => new() { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = amount };

    // Each payment by another method is other money, with a reference of its own (BR-301).
    private static RecordPaymentRequest Upi(PayerDto payer, decimal amount)
        => new()
        {
            PayerId = payer.Id,
            PaymentDate = Today,
            Method = PaymentMethod.UPI,
            Reference = (500_000_000_000L + Interlocked.Increment(ref _references)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Amount = amount,
        };

    private static async Task<T> CreatedAsync<T>(HttpClient client, Uri collection, object request)
        where T : class
    {
        using var response = await client.PostJsonAsync(collection, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    private static async Task<T> ReadAsync<T>(HttpClient client, string pathAndQuery)
        where T : class
    {
        using var response = await client.GetAsync(new Uri(pathAndQuery, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    private static Task<CashierShiftDto> ShiftAsync(HttpClient client, Guid id) => ReadAsync<CashierShiftDto>(client, $"/api/v1/cashier-shifts/{id}");

    private static async Task<IReadOnlyList<CashierShiftDto>> ListAsync(HttpClient client, string query)
        => (await ReadAsync<PageOf<CashierShiftDto>>(client, $"/api/v1/cashier-shifts?{query}")).Items;

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid shiftId)
        => (await ReadAsync<PageOf<AuditRecordDto>>(client, $"/api/v1/audit-records?entityType=CashierShift&entityId={shiftId}")).Items;

    // A refund drafted and submitted by the maker and approved by the checker.
    private static async Task<RefundDto> ApprovedRefundAsync(HttpClient maker, HttpClient checker, CreateRefundRequest request)
    {
        var draft = await CreatedAsync<RefundDto>(maker, Refunds, request);
        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/refunds/{draft.Id}/post", UriKind.Relative), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<RefundDto>();
        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/refunds/{draft.Id}/approve", UriKind.Relative), body: null, PayersApi.ETag(waiting.Version));

        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        return await approved.ReadAsAsync<RefundDto>();
    }

    // The reversal of a payment asked for by the maker and approved by the checker.
    private static async Task ReversedAsync(HttpClient maker, HttpClient checker, Guid paymentId)
    {
        var current = await ReadAsync<PaymentDto>(maker, $"/api/v1/payments/{paymentId}");
        using var asked = await maker.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"/api/v1/payments/{paymentId}/reverse", UriKind.Relative), new RequestReversalRequest { Reason = "Recorded by mistake" }, PayersApi.ETag(current.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.ReadAsAsync<PaymentAllocationsDto>();
        using var approved = await checker.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"/api/v1/payments/{paymentId}/approve-reversal", UriKind.Relative), body: null, PayersApi.ETag(waiting.Payment.Version));

        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
    }

    // A tenant without an approval threshold and two of its users.
    private async Task<(FixedIdentity Maker, FixedIdentity Checker)> NewMakerAndCheckerAsync()
    {
        var maker = await NewApiTenantAsync();

        return (maker, new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())));
    }

    // The Cash entries of the postings that name the shift, debits less credits, as the ledger's own tables have them.
    private Task<decimal> LedgerOfShiftAsync(Guid shiftId)
        => DecimalAsync(
            $"""
            SELECT COALESCE(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e JOIN dbo.LedgerPostings p ON p.[Id] = e.[PostingId]
            WHERE e.[Account] = 'Cash' AND p.[ShiftId] = '{shiftId}'
            """);

    private async Task<decimal> DecimalAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<decimal>(sql).SingleAsync();
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }
}
