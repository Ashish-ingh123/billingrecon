using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payments;

/// <summary>
/// The use cases of the shift with their ports replaced (US-45 to US-47; BR-310 to BR-315; ADR 0060): one shift at a
/// time, a closing on the cash the ledger answers, a difference that opens an approval request its cashier cannot
/// decide, and the cash payment that needs the shift, takes its date and names it in its posting.
/// </summary>
public sealed class CashierShiftHandlerTests
{
    private static readonly UserId Cashier = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly CashierShiftId Id = new(Guid.NewGuid());
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);

    private readonly ICashierShiftRepository _shifts = Substitute.For<ICashierShiftRepository>();
    private readonly IShiftCash _shiftCash = Substitute.For<IShiftCash>();
    private readonly IUserNames _userNames = Substitute.For<IUserNames>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICorrelationContext _correlation = Substitute.For<ICorrelationContext>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly List<ApprovalRequest> _opened = [];
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public CashierShiftHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _currentUser.UserId.Returns(Cashier);
        _currentUser.Roles.Returns(new[] { Role.Cashier });
        _correlation.CorrelationId.Returns(Guid.NewGuid());
        _userNames.FindDisplayNameAsync(Cashier, Arg.Any<CancellationToken>()).Returns("Asha Rao");
        _shiftCash.OfAsync(Arg.Any<CashierShiftId>(), Arg.Any<CancellationToken>()).Returns(ShiftCash.None);
        _approvals.Add(Arg.Do<ApprovalRequest>(_opened.Add));
    }

    [Fact]
    public async Task US45_a_shift_is_opened_for_the_user_who_asks_on_todays_date_and_committed_once()
    {
        CashierShift? added = null;
        _shifts.Add(Arg.Do<CashierShift>(shift => added = shift));

        var result = await Open().HandleAsync(new OpenCashierShiftCommand(500m), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.CashierUserId, result.Value.CashierName, result.Value.BusinessDate, result.Value.Status, result.Value.OpeningFloat, result.Value.ExpectedCash)
            .ShouldBe((Cashier.Value, "Asha Rao", TestDoubles.Today, CashierShiftStatus.Open, 500m, 500m));
        added.ShouldNotBeNull().CashierUserId.ShouldBe(Cashier);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR310_a_second_shift_is_refused_by_the_check_and_for_two_at_once_by_the_index()
    {
        _shifts.AnyUnclosedOfAsync(Cashier, Arg.Any<CancellationToken>()).Returns(true);
        var found = await Open().HandleAsync(new OpenCashierShiftCommand(0m), CancellationToken.None);

        _shifts.AnyUnclosedOfAsync(Cashier, Arg.Any<CancellationToken>()).Returns(false);
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());
        var raced = await Open().HandleAsync(new OpenCashierShiftCommand(0m), CancellationToken.None);

        found.Error.Code.ShouldBe(PaymentsErrors.ShiftAlreadyOpenCode);
        raced.Error.Code.ShouldBe(PaymentsErrors.ShiftAlreadyOpenCode);
        raced.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-0.01)]
    [InlineData(0.001)]
    public async Task A_float_that_is_missing_below_zero_or_finer_than_a_paisa_is_refused_and_nothing_is_written(double? openingFloat)
    {
        var result = await Open().HandleAsync(new OpenCashierShiftCommand((decimal?)openingFloat), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.Error.FieldErrors.Keys.ShouldBe(["openingFloat"]);
        _shifts.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task BR312_BR313_a_closing_counts_against_the_float_and_the_cash_the_ledger_answers_for_the_shift()
    {
        var shift = Opened(500m);
        _shiftCash.OfAsync(Id, Arg.Any<CancellationToken>()).Returns(new ShiftCash(Money.Of(300.50m), Money.Of(100m)));

        var result = await Close().HandleAsync(Counted([(500, 1), (200, 1)], 0.50m, reason: null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Status, result.Value.CashReceived, result.Value.CashPaidOut, result.Value.ExpectedCash, result.Value.CountedCash, result.Value.Difference)
            .ShouldBe((CashierShiftStatus.Closed, 300.50m, 100m, 700.50m, 700.50m, 0m));
        shift.Status.ShouldBe(CashierShiftStatus.Closed);
        _opened.ShouldBeEmpty("a closing without a difference needs no second person (BR-314)");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR314_a_closing_with_a_difference_opens_a_request_of_its_cashier_in_the_same_commit()
    {
        var shift = Opened(500m);
        _shiftCash.OfAsync(Id, Arg.Any<CancellationToken>()).Returns(new ShiftCash(Money.Of(200m), Money.Zero));

        var silent = await Close().HandleAsync(Counted([(500, 1), (100, 1), (50, 1)], 0m, reason: null), CancellationToken.None);
        var result = await Close().HandleAsync(Counted([(500, 1), (100, 1), (50, 1)], 0m, "A note is missing"), CancellationToken.None);

        silent.Error.Code.ShouldBe(PaymentsErrors.ShiftDifferenceNeedsReasonCode);
        (result.Value.Status, result.Value.Difference, result.Value.DifferenceReason).ShouldBe((CashierShiftStatus.PendingApproval, -50m, "A note is missing"));
        var request = _opened.ShouldHaveSingleItem();
        (request.DocumentType, request.DocumentId, request.Action, request.MakerUserId, request.MakerComment, request.Status)
            .ShouldBe((ApprovalDocumentType.CashierShift, Id.Value, ApprovalAction.Close, Cashier, "A note is missing", ApprovalStatus.Pending));
        shift.Status.ShouldBe(CashierShiftStatus.PendingApproval);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_closing_of_an_unknown_shift_of_another_cashier_or_on_a_stale_version_writes_nothing()
    {
        Opened(100m);
        var command = Counted([(100, 1)], 0m, reason: null);

        var unknown = await Close().HandleAsync(command with { Id = new CashierShiftId(Guid.NewGuid()) }, CancellationToken.None);
        var stale = await Close().HandleAsync(command with { ExpectedVersion = Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]) }, CancellationToken.None);
        _currentUser.UserId.Returns(Checker);
        var notOwn = await Close().HandleAsync(command, CancellationToken.None);

        unknown.Error.Code.ShouldBe(PaymentsErrors.ShiftNotFoundCode);
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        (notOwn.Error.Code, notOwn.Error.Kind).ShouldBe((PaymentsErrors.ShiftNotOwnCode, ErrorKind.Forbidden));
        await _shiftCash.DidNotReceiveWithAnyArgs().OfAsync(default(CashierShiftId), default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR603_the_cashier_of_a_shift_decides_nothing_and_a_second_person_approves_or_rejects()
    {
        var (shift, request) = Waiting();

        // The cashier holds the role of a checker, and is still the maker.
        _currentUser.Roles.Returns(new[] { Role.Cashier, Role.Supervisor });
        var own = await Approve().HandleAsync(new ApproveCashierShiftClosingCommand(Id, Version(), null), CancellationToken.None);
        var ownRejection = await Reject().HandleAsync(new RejectCashierShiftClosingCommand(Id, Version(), "No"), CancellationToken.None);

        own.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        ownRejection.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        (shift.Status, request.Status).ShouldBe((CashierShiftStatus.PendingApproval, ApprovalStatus.Pending));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);

        // A cashier who is not a checker is refused by the role (BR-603).
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.Cashier });
        (await Approve().HandleAsync(new ApproveCashierShiftClosingCommand(Id, Version(), null), CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ApprovalCheckerRoleCode);

        _currentUser.Roles.Returns(new[] { Role.Supervisor });
        var approved = await Approve().HandleAsync(new ApproveCashierShiftClosingCommand(Id, Version(), " Seen "), CancellationToken.None);

        (approved.Value.Status, approved.Value.Difference).ShouldBe((CashierShiftStatus.Closed, -50m));
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, Checker, "Seen"));
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR605_a_rejection_needs_its_comment_and_opens_the_shift_again()
    {
        var (shift, request) = Waiting();
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.TenantAdmin });

        var wordless = await Reject().HandleAsync(new RejectCashierShiftClosingCommand(Id, Version(), " "), CancellationToken.None);
        var rejected = await Reject().HandleAsync(new RejectCashierShiftClosingCommand(Id, Version(), "Count it again"), CancellationToken.None);

        wordless.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        (rejected.Value.Status, rejected.Value.Count, rejected.Value.Difference).ShouldBe((CashierShiftStatus.Open, null, null));
        (shift.Status, request.Status, request.CheckerComment).ShouldBe((CashierShiftStatus.Open, ApprovalStatus.Rejected, "Count it again"));
    }

    [Fact]
    public async Task A_decision_on_a_shift_that_does_not_wait_or_whose_request_is_gone_is_refused()
    {
        Opened(100m);
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.Supervisor });
        var notWaiting = await Approve().HandleAsync(new ApproveCashierShiftClosingCommand(Id, Version(), null), CancellationToken.None);

        // The shift waits, and its request was decided by a request that committed in between: a conflict (BR-804).
        Waiting();
        _approvals.GetPendingAsync(ApprovalDocumentType.CashierShift, Id.Value, Arg.Any<CancellationToken>()).Returns((ApprovalRequest?)null);
        var decidedMeanwhile = await Reject().HandleAsync(new RejectCashierShiftClosingCommand(Id, Version(), "No"), CancellationToken.None);

        notWaiting.Error.Code.ShouldBe(PaymentsErrors.ShiftNotPendingApprovalCode);
        decidedMeanwhile.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_cashier_reads_and_lists_their_own_shifts_and_a_supervisor_all()
    {
        var reads = Substitute.For<ICashierShiftReadService>();
        var theirs = Opened(0m).ToDto(ShiftCash.None, "Asha Rao") with { CashierUserId = Checker.Value };
        reads.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(theirs);
        ListCashierShiftsQuery? asked = null;
        reads.ListAsync(Arg.Do<ListCashierShiftsQuery>(query => asked = query), Arg.Any<CancellationToken>())
            .Returns(new PageOf<CashierShiftDto>([], 1, 20, 0));
        var get = new GetCashierShiftHandler(reads, _currentUser);
        var list = new ListCashierShiftsHandler(new ListCashierShiftsQueryValidator(), reads, _currentUser);

        // A cashier: another cashier's shift is not found, and the list is their own whatever they ask.
        (await get.HandleAsync(new GetCashierShiftQuery(Id), CancellationToken.None)).Error.Code.ShouldBe(PaymentsErrors.ShiftNotFoundCode);
        await list.HandleAsync(new ListCashierShiftsQuery { CashierUserId = Checker }, CancellationToken.None);
        asked.ShouldNotBeNull().CashierUserId.ShouldBe(Cashier);

        // A supervisor reads it, lists whose they ask for, and their own with "mine".
        _currentUser.Roles.Returns(new[] { Role.Supervisor });
        (await get.HandleAsync(new GetCashierShiftQuery(Id), CancellationToken.None)).Value.ShouldBe(theirs);
        await list.HandleAsync(new ListCashierShiftsQuery { CashierUserId = Checker }, CancellationToken.None);
        asked.CashierUserId.ShouldBe(Checker);
        await list.HandleAsync(new ListCashierShiftsQuery { Mine = true }, CancellationToken.None);
        asked.CashierUserId.ShouldBe(Cashier);
        (await list.HandleAsync(new ListCashierShiftsQuery { PageSize = 101 }, CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
    }

    [Fact]
    public async Task BR311_a_cash_payment_needs_the_open_shift_of_who_records_it_takes_its_date_and_names_it_in_the_ledger()
    {
        var payments = Substitute.For<IPaymentRepository>();
        var settings = Substitute.For<ITenantSettingsReader>();
        var payers = Substitute.For<IActivePayers>();
        var numbers = Substitute.For<INumberSeries>();
        var ledger = Substitute.For<IReceivablesLedger>();
        var posted = new List<PostingTemplate>();
        settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        numbers.NextAsync(DocumentType.Payment, Arg.Any<FiscalYear>(), Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.Payment, Fy, "PAY", 1));
        ledger.PostAsync(Arg.Do<PostingTemplate>(posted.Add), Arg.Any<CancellationToken>()).Returns(_ => (Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
        var handler = new RecordPaymentHandler(new RecordPaymentCommandValidator(), _clock, _currentUser, payments, _shifts, settings, payers, numbers, ledger, _unitOfWork);
        var cash = new RecordPaymentCommand { PayerId = Payer, Method = PaymentMethod.Cash, Amount = 40m };

        // No open shift: refused before the number is taken (ADR 0022).
        var without = await handler.HandleAsync(cash, CancellationToken.None);
        without.Error.Code.ShouldBe(PaymentsErrors.ShiftNotOpenCode);
        await numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);

        // A shift opened yesterday and still open: cash sent without a date is dated the day of the shift.
        var yesterday = TestDoubles.Today.AddDays(-1);
        var shift = CashierShift.Open(Cashier, yesterday, Money.Zero, _clock.UtcNow.AddDays(-1)).WithId<CashierShift, CashierShiftId>(Id);
        _shifts.FindOpenOfAsync(Cashier, Arg.Any<CancellationToken>()).Returns(shift);
        var undated = await handler.HandleAsync(cash, CancellationToken.None);
        var otherDay = await handler.HandleAsync(cash with { PaymentDate = TestDoubles.Today }, CancellationToken.None);

        undated.Value.PaymentDate.ShouldBe(yesterday);
        otherDay.Error.Code.ShouldBe(PaymentsErrors.ShiftDateMismatchCode);
        var template = posted.ShouldHaveSingleItem();
        (template.Kind, template.ShiftId, template.BusinessDate).ShouldBe((LedgerPostingKind.PaymentRecorded, Id, yesterday));
        shift.CashDocuments.ShouldBe(1);

        // Money by another method names no shift and asks for none.
        _shifts.ClearReceivedCalls();
        var upi = await handler.HandleAsync(cash with { Method = PaymentMethod.UPI, Reference = "412345678901" }, CancellationToken.None);
        upi.Value.PaymentDate.ShouldBe(TestDoubles.Today);
        posted[^1].ShiftId.ShouldBeNull();
        await _shifts.DidNotReceiveWithAnyArgs().FindOpenOfAsync(default, default);
    }

    private OpenCashierShiftHandler Open() => new(new OpenCashierShiftCommandValidator(), _clock, _currentUser, _shifts, _shiftCash, _userNames, _unitOfWork);

    private CloseCashierShiftHandler Close()
        => new(new CloseCashierShiftCommandValidator(), _clock, _currentUser, _correlation, _shifts, _shiftCash, _approvals, _userNames, _unitOfWork);

    private ApproveCashierShiftClosingHandler Approve()
        => new(new ApproveCashierShiftClosingCommandValidator(), _clock, _currentUser, _shifts, _approvals, _shiftCash, _userNames, _unitOfWork);

    private RejectCashierShiftClosingHandler Reject()
        => new(new RejectCashierShiftClosingCommandValidator(), _clock, _currentUser, _shifts, _approvals, _shiftCash, _userNames, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private static CloseCashierShiftCommand Counted((int Denomination, int Pieces)[] pieces, decimal looseChange, string? reason)
        => new(Id, Version(), [.. pieces.Select(piece => new CashCountInput(piece.Denomination, piece.Pieces))], looseChange, reason);

    // An open shift of the cashier, as the repository answers it.
    private CashierShift Opened(decimal openingFloat)
    {
        var shift = CashierShift.Open(Cashier, TestDoubles.Today, Money.Of(openingFloat), _clock.UtcNow)
            .WithId<CashierShift, CashierShiftId>(Id)
            .Saved(CurrentVersion);
        shift.ClearDomainEvents();
        _shifts.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(shift);

        return shift;
    }

    // A shift counted 50.00 short, with the pending request its closing opened.
    private (CashierShift Shift, ApprovalRequest Request) Waiting()
    {
        var shift = Opened(500m);
        shift.Close(CashCount.Of([(500, 1), (100, 1), (50, 1)]), Money.Zero, Money.Of(200m), "A note is missing", _clock.UtcNow).IsSuccess.ShouldBeTrue();
        shift.ClearDomainEvents();
        var request = ApprovalRequest.Open(ApprovalDocumentType.CashierShift, Id.Value, ApprovalAction.Close, Cashier, "A note is missing", _clock.UtcNow, Guid.NewGuid());
        _approvals.GetPendingAsync(ApprovalDocumentType.CashierShift, Id.Value, Arg.Any<CancellationToken>()).Returns(request);

        return (shift, request);
    }
}
