using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Reporting;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Reporting;

/// <summary>
/// The use cases of the reports (BR-901, US-40, US-42; ADR 0036): the shape of what they are asked, the period a register
/// covers when none is given, the date of a collection, and the payer of a balance found through the Payers module.
/// What each report reads is tested against SQL Server (<c>ReportsApiTests</c>).
/// </summary>
public sealed class ReportingHandlerTests
{
    private static readonly IClock Clock = TestDoubles.ClockAt(TestDoubles.Today);

    [Fact]
    public void Without_a_period_a_register_covers_the_current_month_in_India()
    {
        ReportPeriods.Resolve(null, null, new DateOnly(2026, 10, 17)).ShouldBe(new ReportPeriodDto(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        ReportPeriods.Resolve(null, null, new DateOnly(2028, 2, 29)).ShouldBe(new ReportPeriodDto(new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)));
        ReportPeriods.Resolve(null, null, new DateOnly(2026, 12, 31)).ShouldBe(new ReportPeriodDto(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)));
        ReportPeriods.Resolve(new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new DateOnly(2026, 10, 17))
            .ShouldBe(new ReportPeriodDto(new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)));
    }

    [Theory]
    [InlineData("2026-10-01", null, "Give both from and to")]
    [InlineData(null, "2026-10-31", "Give both from and to")]
    [InlineData("2026-10-31", "2026-10-01", "not before its start")]
    [InlineData("2027-04-01", "2028-04-01", "at most 366 days")]
    public async Task A_register_refuses_a_period_it_cannot_cover(string? from, string? to, string message)
    {
        var reports = RegisterReports();
        var bills = new BillRegisterHandler(new BillRegisterQueryValidator(), reports, Clock);
        var payments = new PaymentRegisterHandler(new PaymentRegisterQueryValidator(), reports, Clock);

        var billResult = await bills.HandleAsync(new BillRegisterQuery { From = Date(from), To = Date(to) }, CancellationToken.None);
        var paymentResult = await payments.HandleAsync(new PaymentRegisterQuery { From = Date(from), To = Date(to) }, CancellationToken.None);

        foreach (var error in new[] { billResult.Error, paymentResult.Error })
        {
            error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
            error.FieldErrors["to"].ShouldHaveSingleItem().ShouldContain(message);
        }

        await reports.DidNotReceiveWithAnyArgs().BillRegisterAsync(default!, default!, default);
        await reports.DidNotReceiveWithAnyArgs().PaymentRegisterAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("2026-04-01", "2027-03-31")]
    [InlineData("2027-04-01", "2028-03-31")]
    [InlineData("2026-10-17", "2026-10-17")]
    public async Task A_register_covers_a_day_or_up_to_a_year_a_leap_year_included(string from, string to)
    {
        var reports = RegisterReports();
        var handler = new BillRegisterHandler(new BillRegisterQueryValidator(), reports, Clock);

        var result = await handler.HandleAsync(new BillRegisterQuery { From = Date(from), To = Date(to) }, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await reports.Received(1).BillRegisterAsync(new ReportPeriodDto(Date(from)!.Value, Date(to)!.Value), Arg.Any<BillRegisterQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_register_asked_for_no_period_reads_the_current_month()
    {
        var reports = RegisterReports();
        var handler = new PaymentRegisterHandler(new PaymentRegisterQueryValidator(), reports, Clock);

        var result = await handler.HandleAsync(new PaymentRegisterQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await reports.Received(1).PaymentRegisterAsync(
            new ReportPeriodDto(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), Arg.Any<PaymentRegisterQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_register_refuses_filters_it_does_not_know()
    {
        var reports = RegisterReports();
        var bills = new BillRegisterHandler(new BillRegisterQueryValidator(), reports, Clock);
        var payments = new PaymentRegisterHandler(new PaymentRegisterQueryValidator(), reports, Clock);

        var status = await bills.HandleAsync(new BillRegisterQuery { Status = (ReportBillStatus)9 }, CancellationToken.None);
        var payer = await bills.HandleAsync(new BillRegisterQuery { PayerId = new PayerId(Guid.Empty) }, CancellationToken.None);
        var method = await payments.HandleAsync(new PaymentRegisterQuery { Method = (ReportPaymentMethod)9 }, CancellationToken.None);
        var page = await payments.HandleAsync(new PaymentRegisterQuery { PageSize = Paging.MaxPageSize + 1 }, CancellationToken.None);

        status.Error.FieldErrors.Keys.ShouldBe(["status"]);
        payer.Error.FieldErrors.Keys.ShouldBe(["payerId"]);
        method.Error.FieldErrors.Keys.ShouldBe(["method"]);
        page.Error.FieldErrors.Keys.ShouldBe(["pageSize"]);
    }

    [Fact]
    public async Task The_collection_is_today_s_in_India_unless_another_date_is_asked_for()
    {
        var reports = CollectionReports();
        var handler = new DailyCollectionHandler(reports, Clock);

        (await handler.HandleAsync(new DailyCollectionQuery(), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(new DailyCollectionQuery { Date = new DateOnly(2026, 9, 30) }, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await reports.Received(1).DailyCollectionAsync(TestDoubles.Today, Arg.Any<CancellationToken>());
        await reports.Received(1).DailyCollectionAsync(new DateOnly(2026, 9, 30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_payer_s_balance_is_read_for_a_payer_the_Payers_module_finds_and_refused_as_it_refuses_otherwise()
    {
        var known = new PayerId(Guid.NewGuid());
        var unknown = new PayerId(Guid.NewGuid());
        var payers = Substitute.For<IQueryHandler<GetPayerQuery, PayerDto>>();
        payers.HandleAsync(new GetPayerQuery(known), Arg.Any<CancellationToken>()).Returns(Result.Success(Payer(known, "Ravi Iyer")));
        payers.HandleAsync(new GetPayerQuery(unknown), Arg.Any<CancellationToken>()).Returns(Result.Failure<PayerDto>(PayersErrors.PayerNotFound()));
        var reports = BalanceReports();
        var handler = new GetPayerBalanceHandler(payers, reports);

        var found = await handler.HandleAsync(new GetPayerBalanceQuery(known), CancellationToken.None);
        var missing = await handler.HandleAsync(new GetPayerBalanceQuery(unknown), CancellationToken.None);

        found.IsSuccess.ShouldBeTrue();
        await reports.Received(1).PayerBalanceAsync(known, "Ravi Iyer", Arg.Any<CancellationToken>());
        missing.Error.Code.ShouldBe(PayersErrors.PayerNotFoundCode);
        await reports.DidNotReceive().PayerBalanceAsync(unknown, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_pending_approvals_are_counted_in_days_from_today_in_India()
    {
        var reports = ApprovalReports();
        var handler = new PendingApprovalsHandler(new PendingApprovalsQueryValidator(), reports, Clock);

        var result = await handler.HandleAsync(new PendingApprovalsQuery { DocumentType = ApprovalDocumentType.Refund }, CancellationToken.None);
        var unknownType = await handler.HandleAsync(new PendingApprovalsQuery { DocumentType = (ApprovalDocumentType)99 }, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await reports.Received(1).PendingApprovalsAsync(Arg.Is<PendingApprovalsQuery>(query => query.DocumentType == ApprovalDocumentType.Refund), TestDoubles.Today, Arg.Any<CancellationToken>());
        unknownType.Error.FieldErrors.Keys.ShouldBe(["documentType"]);
    }

    [Fact]
    public async Task The_balance_reports_refuse_a_search_too_long_and_an_empty_payer_id()
    {
        var reports = BalanceReports();
        var outstanding = new OutstandingByPayerHandler(new OutstandingByPayerQueryValidator(), reports);
        var unapplied = new UnappliedCashHandler(new UnappliedCashQueryValidator(), reports);
        var credit = new AvailableCreditHandler(new AvailableCreditQueryValidator(), reports);

        var search = await outstanding.HandleAsync(new OutstandingByPayerQuery { Search = new string('a', OutstandingByPayerQuery.SearchMaxLength + 1) }, CancellationToken.None);
        var unappliedPayer = await unapplied.HandleAsync(new UnappliedCashQuery { PayerId = new PayerId(Guid.Empty) }, CancellationToken.None);
        var creditPayer = await credit.HandleAsync(new AvailableCreditQuery { PayerId = new PayerId(Guid.Empty) }, CancellationToken.None);

        search.Error.FieldErrors.Keys.ShouldBe(["search"]);
        unappliedPayer.Error.FieldErrors.Keys.ShouldBe(["payerId"]);
        creditPayer.Error.FieldErrors.Keys.ShouldBe(["payerId"]);
        reports.ReceivedCalls().ShouldBeEmpty();
    }

    private static DateOnly? Date(string? text) => text is null ? null : DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    // Ports that answer an empty report, as a tenant with no documents gets.
    private static IRegisterReports RegisterReports()
    {
        var reports = Substitute.For<IRegisterReports>();
        reports.BillRegisterAsync(default!, default!, default)
            .ReturnsForAnyArgs(call => Empty<BillRegisterRowDto, BillRegisterTotalsDto>(call.Arg<ReportPeriodDto>(), new BillRegisterTotalsDto(0, 0m, 0m)));
        reports.PaymentRegisterAsync(default!, default!, default)
            .ReturnsForAnyArgs(call => Empty<PaymentRegisterRowDto, PaymentRegisterTotalsDto>(call.Arg<ReportPeriodDto>(), new PaymentRegisterTotalsDto(0, 0m, 0m)));

        return reports;
    }

    private static ICollectionReports CollectionReports()
    {
        var reports = Substitute.For<ICollectionReports>();
        reports.DailyCollectionAsync(default, default).ReturnsForAnyArgs(call => new DailyCollectionDto(call.Arg<DateOnly>(), [], new CollectionTotalsDto(0, 0m, 0m, 0m, 0m)));

        return reports;
    }

    private static IApprovalReports ApprovalReports()
    {
        var reports = Substitute.For<IApprovalReports>();
        reports.PendingApprovalsAsync(default!, default, default).ReturnsForAnyArgs(Empty<PendingApprovalDto, PendingApprovalsTotalsDto>(null, new PendingApprovalsTotalsDto(0, null)));

        return reports;
    }

    private static IBalanceReports BalanceReports()
    {
        var reports = Substitute.For<IBalanceReports>();
        reports.PayerBalanceAsync(default, default!, default).ReturnsForAnyArgs(call => new PayerOutstandingDto(call.Arg<PayerId>().Value, call.Arg<string>(), 0m, 0m, 0m, 0m));

        return reports;
    }

    private static ReportPage<TItem, TTotals> Empty<TItem, TTotals>(ReportPeriodDto? period, TTotals totals) => new(period, [], 1, Paging.DefaultPageSize, 0, totals);

    private static PayerDto Payer(PayerId id, string name) => new(
        id.Value, PayerKind.SelfPay, name, null, null, null, PayerStatus.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "AAAAAAAAAAE=");
}
